using Marketplace.Application.Common.Extensions;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Reviews.Abstractions;
using Marketplace.Application.Modules.Reviews.DTOs;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Events;
using Marketplace.Domain.Identity;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Reviews;
using Marketplace.Domain.Sellers;
using Microsoft.EntityFrameworkCore;
using OrderEntity = Marketplace.Domain.Orders.Order;

namespace Marketplace.Application.Modules.Reviews.Services;

/// <summary>
/// Reviews with a verified-purchase rule: only the buyer of a delivered order item may
/// review it, once. The rule lives in the domain and is also backed by a unique index
/// on <c>order_item_id</c>.
/// </summary>
public sealed class ReviewService(
    IRepository<Review> reviews,
    IRepository<OrderEntity> orders,
    IRepository<Domain.Orders.OrderItem> orderItems,
    IRepository<Domain.Catalog.Product> products,
    IRepository<User> users,
    IRepository<SellerStore> stores,
    IRepository<Seller> sellers,
    ICacheService cache,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    IClock clock,
    IAuditService auditService,
    INotificationService notificationService,
    IRealtimeNotifier realtime) : IReviewService
{
    public async Task<PagedResult<ReviewResponse>> ListForProductAsync(Guid productId, ReviewListQuery query, CancellationToken cancellationToken = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var source = reviews.Query().AsNoTracking().Where(r => r.ProductId == productId && r.IsVisible);

        if (query.MinRating is { } min)
        {
            source = source.Where(r => r.Rating >= min);
        }

        source = query.Sort switch
        {
            "rating" => source.OrderByDescending(r => r.Rating).ThenByDescending(r => r.CreatedAt),
            "helpful" => source.OrderByDescending(r => r.HelpfulCount),
            "oldest" => source.OrderBy(r => r.CreatedAt),
            _ => source.OrderByDescending(r => r.CreatedAt)
        };

        var result = await source.ToPagedResultAsync(page, r => new ReviewResponse(
            r.Id, r.ProductId, string.Empty, string.Empty, null, r.Rating, r.Title, r.Body,
            r.IsVerifiedPurchase, r.IsVisible, r.HelpfulCount, string.Empty, r.CreatedAt, null), cancellationToken)
            .ConfigureAwait(false);

        return await HydrateAsync(result, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PagedResult<ReviewResponse>> ListForSellerAsync(ReviewListQuery query, CancellationToken cancellationToken = default)
    {
        if (currentUser.SellerId is not { } sellerId)
        {
            return PagedResult<ReviewResponse>.Empty(query.Page ?? 1, query.PageSize ?? 20);
        }

        var page = new PageRequest(query.Page, query.PageSize);
        var productIds = products.Query().Where(p => p.SellerId == sellerId).Select(p => p.Id);
        var source = reviews.Query().AsNoTracking().Where(r => productIds.Contains(r.ProductId));

        if (query.VisibleOnly is not null)
        {
            source = source.Where(r => r.IsVisible == query.VisibleOnly.Value);
        }

        if (query.MinRating is { } min)
        {
            source = source.Where(r => r.Rating >= min);
        }

        var result = await source
            .OrderByDescending(r => r.CreatedAt)
            .ToPagedResultAsync(page, r => new ReviewResponse(
                r.Id, r.ProductId, string.Empty, string.Empty, null, r.Rating, r.Title, r.Body,
                r.IsVerifiedPurchase, r.IsVisible, r.HelpfulCount, string.Empty, r.CreatedAt, null), cancellationToken)
            .ConfigureAwait(false);

        return await HydrateAsync(result, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ReviewResponse>> CreateAsync(Guid productId, CreateReviewRequest request, CancellationToken cancellationToken = default)
    {
        var orderItem = await orderItems.Query().AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == request.OrderItemId, cancellationToken)
            .ConfigureAwait(false);

        var order = orderItem is null
            ? null
            : await orders.Query().AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderItem.OrderId, cancellationToken).ConfigureAwait(false);

        var alreadyExists = await reviews.AnyAsync(r => r.OrderItemId == request.OrderItemId, cancellationToken).ConfigureAwait(false);

        var eligibility = ReviewEligibility.Evaluate(orderItem, order, currentUser.UserId, alreadyExists, clock.UtcNow);
        if (!eligibility.CanReview)
        {
            return Result<ReviewResponse>.Failure(eligibility.Reason ?? "This purchase cannot be reviewed.");
        }

        var product = await products.Query().AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return Result<ReviewResponse>.Failure("Product not found.", ResultErrorCodes.NotFound);
        }

        if (orderItem!.ProductId != productId)
        {
            return Result<ReviewResponse>.Failure("This order item belongs to a different product.");
        }

        var now = clock.UtcNow;
        var review = Review.Create(productId, orderItem.Id, orderItem.OrderId, currentUser.UserId, product.SellerId,
            request.Rating, request.Title, request.Body, isVerifiedPurchase: true, now);

        review.AddDomainEvent(new ReviewCreatedEvent(review.Id, productId, product.SellerId, request.Rating, now));

        await reviews.AddAsync(review, cancellationToken).ConfigureAwait(false);
        orderItem!.MarkReviewed();

        await RecalculateProductRatingAsync(productId, now, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // A product's detail carries its rating and its reviews, and it is cached, so a new review
        // is invisible on the product page until the entry happens to expire. The tag covers both
        // the id and the slug entries, which were written under it.
        await cache.RemoveByTagAsync(CacheKeys.ProductTag(productId), cancellationToken).ConfigureAwait(false);
        await cache.RemoveAsync(CacheKeys.Product(productId), cancellationToken).ConfigureAwait(false);
        await cache.RemoveAsync(CacheKeys.ProductBySlug(product.SlugValue), cancellationToken).ConfigureAwait(false);
        await cache.RemoveByTagAsync(CacheKeys.CatalogTag, cancellationToken).ConfigureAwait(false);

        await auditService.RecordAsync(AuditAction.ReviewCreated, nameof(Review), review.Id, product.Name,
            new { productId, request.Rating }, cancellationToken).ConfigureAwait(false);

        var seller = await FindSellerUserAsync(product.SellerId, cancellationToken).ConfigureAwait(false);
        if (seller is not null)
        {
            await notificationService.NotifySellerAsync(seller.Value, NotificationType.NewReview,
                "New product review",
                $"\"{product.Name}\" received a {request.Rating}-star review.",
                "/seller/reviews", cancellationToken).ConfigureAwait(false);

            await realtime.InventoryLowAsync(product.SellerId, new { Type = "NewReview", review.Id, productId }, cancellationToken).ConfigureAwait(false);
        }

        return await GetByIdAsync(review.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ReviewResponse>> UpdateAsync(Guid reviewId, UpdateReviewRequest request, CancellationToken cancellationToken = default)
    {
        var review = await reviews.Query().FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken).ConfigureAwait(false);
        if (review is null)
        {
            return Result<ReviewResponse>.Failure("Review not found.", ResultErrorCodes.NotFound);
        }

        if (review.CustomerId != currentUser.UserId && !currentUser.IsAdmin)
        {
            return Result<ReviewResponse>.Failure("You can only edit your own review.");
        }

        var now = clock.UtcNow;
        review.Update(request.Rating, request.Title, request.Body, now);

        await RecalculateProductRatingAsync(review.ProductId, now, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.ReviewUpdated, nameof(Review), review.Id, null,
            new { request.Rating }, cancellationToken).ConfigureAwait(false);

        return await GetByIdAsync(review.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result> DeleteAsync(Guid reviewId, CancellationToken cancellationToken = default)
    {
        var review = await reviews.Query().FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken).ConfigureAwait(false);
        if (review is null)
        {
            return Result.Failure("Review not found.", ResultErrorCodes.NotFound);
        }

        if (review.CustomerId != currentUser.UserId && !currentUser.IsAdmin)
        {
            return Result.Failure("You can only delete your own review.");
        }

        reviews.Remove(review);
        await RecalculateProductRatingAsync(review.ProductId, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.ReviewDeleted, nameof(Review), review.Id, null, null, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    public async Task<Result<ReviewResponse>> ModerateAsync(Guid reviewId, ModerateReviewRequest request, CancellationToken cancellationToken = default)
    {
        var review = await reviews.Query().FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken).ConfigureAwait(false);
        if (review is null)
        {
            return Result<ReviewResponse>.Failure("Review not found.", ResultErrorCodes.NotFound);
        }

        var now = clock.UtcNow;
        review.SetVisibility(request.IsVisible, request.Note, now);

        await RecalculateProductRatingAsync(review.ProductId, now, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.ReviewModerated, nameof(Review), review.Id, null,
            new { request.IsVisible, request.Note }, cancellationToken).ConfigureAwait(false);

        return await GetByIdAsync(review.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ReviewReplyResponse>> ReplyAsync(Guid reviewId, ReplyToReviewRequest request, CancellationToken cancellationToken = default)
    {
        if (currentUser.SellerId is not { } sellerId)
        {
            return Result<ReviewReplyResponse>.Failure("Seller access is required.");
        }

        var review = await reviews.Query().FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken).ConfigureAwait(false);
        if (review is null)
        {
            return Result<ReviewReplyResponse>.Failure("Review not found.", ResultErrorCodes.NotFound);
        }

        var ownsProduct = await products.AnyAsync(p => p.Id == review.ProductId && p.SellerId == sellerId, cancellationToken).ConfigureAwait(false);
        if (!ownsProduct)
        {
            return Result<ReviewReplyResponse>.Failure("You can only reply to reviews of your own products.");
        }

        var reply = ReviewReply.Create(review.Id, sellerId, currentUser.UserId, request.Body, clock.UtcNow);
        review.AttachReply(reply, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var storeName = await stores.Query().AsNoTracking()
            .Where(s => s.SellerId == sellerId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? "Seller";

        return Result<ReviewReplyResponse>.Success(new ReviewReplyResponse(reply.Id, reply.Body, storeName, reply.CreatedAt));
    }

    public async Task<Result> MarkHelpfulAsync(Guid reviewId, CancellationToken cancellationToken = default)
    {
        var review = await reviews.Query().FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken).ConfigureAwait(false);
        if (review is null)
        {
            return Result.Failure("Review not found.", ResultErrorCodes.NotFound);
        }

        review.MarkHelpful();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result<ReviewResponse>> GetByIdAsync(Guid reviewId, CancellationToken cancellationToken)
    {
        var review = await reviews.Query().AsNoTracking().FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken).ConfigureAwait(false);
        if (review is null)
        {
            return Result<ReviewResponse>.Failure("Review not found.", ResultErrorCodes.NotFound);
        }

        var single = new PagedResult<ReviewResponse>(
        [
            new ReviewResponse(review.Id, review.ProductId, string.Empty, string.Empty, null, review.Rating, review.Title,
                review.Body, review.IsVerifiedPurchase, review.IsVisible, review.HelpfulCount, string.Empty, review.CreatedAt,
                review.Reply is null ? null : new ReviewReplyResponse(review.Reply.Id, review.Reply.Body, string.Empty, review.Reply.CreatedAt))
        ], 1, 1, 1);

        var hydrated = await HydrateAsync(single, cancellationToken).ConfigureAwait(false);
        return Result<ReviewResponse>.Success(hydrated.Items[0]);
    }

    private async Task RecalculateProductRatingAsync(Guid productId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var stats = await reviews.Query().AsNoTracking()
            .Where(r => r.ProductId == productId && r.IsVisible)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Average = g.Average(r => (decimal)r.Rating),
                Count = g.Count()
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var product = await products.Query().FirstOrDefaultAsync(p => p.Id == productId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return;
        }

        var reviewCount = await reviews.Query().AsNoTracking().CountAsync(r => r.ProductId == productId, cancellationToken).ConfigureAwait(false);
        product.RecalculateRating(stats?.Average ?? 0m, stats?.Count ?? 0, reviewCount, now);

        if (stats is { Count: > 0 })
        {
            var breakdown = await reviews.Query().AsNoTracking()
                .Where(r => r.ProductId == productId && r.IsVisible)
                .GroupBy(r => r.Rating)
                .Select(g => new { Rating = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var average = decimal.Round(breakdown.Sum(b => b.Rating * b.Count) / (decimal)breakdown.Sum(b => b.Count), 2, MidpointRounding.AwayFromZero);
            var store = await stores.Query().FirstOrDefaultAsync(s => s.SellerId == product.SellerId, cancellationToken).ConfigureAwait(false);
            var storeStats = await reviews.Query().AsNoTracking()
                .Where(r => r.SellerId == product.SellerId && r.IsVisible)
                .GroupBy(_ => 1)
                .Select(g => new { Average = g.Average(r => (decimal)r.Rating), Count = g.Count() })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            store?.RecalculateRating(storeStats?.Average ?? 0m, storeStats?.Count ?? 0, now);
            _ = average;
        }
    }

    /// <summary>
    /// The user id behind a seller, or null when there is no such seller.
    /// </summary>
    /// <remarks>
    /// A notification is addressed to a user, and the notifications table's foreign key points at
    /// the user id. This used to query the store, throw the answer away, and hand back the seller
    /// id instead, so the insert failed on that foreign key and every attempt to write a review
    /// came back as a server error. A seller and the user who owns one are different rows with
    /// different ids, and only the second of them can be written into a notification.
    /// </remarks>
    private async Task<Guid?> FindSellerUserAsync(Guid sellerId, CancellationToken cancellationToken)
    {
        if (sellerId == Guid.Empty)
        {
            return null;
        }

        var seller = await sellers.GetByIdAsync(sellerId, cancellationToken).ConfigureAwait(false);

        return seller?.UserId is { } userId && userId != Guid.Empty ? userId : null;
    }

    private async Task<PagedResult<ReviewResponse>> HydrateAsync(PagedResult<ReviewResponse> result, CancellationToken cancellationToken)
    {
        if (result.Items.Count == 0)
        {
            return result;
        }

        var productIds = result.Items.Select(i => i.ProductId).Distinct().ToList();
        var authorIds = result.Items.Select(i => i.Id).ToList();
        var authorUserIds = await reviews.Query().AsNoTracking()
            .Where(r => authorIds.Contains(r.Id))
            .Select(r => r.CustomerId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var productInfo = await products.Query().AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.SlugValue, p.SellerId, Url = p.Images.Select(i => i.Url).FirstOrDefault() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var productMap = productInfo.ToDictionary(p => p.Id);

        var authorNames = await users.Query().AsNoTracking()
            .Where(u => authorUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => MaskName(u.FirstName), cancellationToken)
            .ConfigureAwait(false);

        var storeNames = await stores.Query().AsNoTracking()
            .ToDictionaryAsync(s => s.SellerId, s => s.Name, cancellationToken)
            .ConfigureAwait(false);

        var raw = await reviews.Query().AsNoTracking()
            .Where(r => authorIds.Contains(r.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var authorByReview = raw.ToDictionary(r => r.Id, r => authorNames.GetValueOrDefault(r.CustomerId, "Customer"));

        var enriched = result.Items.Select(i =>
        {
            var product = productMap.GetValueOrDefault(i.ProductId);
            return i with
            {
                ProductName = product?.Name ?? string.Empty,
                ProductSlug = product?.SlugValue ?? string.Empty,
                ProductImageUrl = product?.Url,
                AuthorName = authorByReview.GetValueOrDefault(i.Id, "Customer"),
                Reply = i.Reply is null ? null : i.Reply with { SellerName = storeNames.GetValueOrDefault(product?.SellerId ?? Guid.Empty, "Seller") }
            };
        }).ToList();

        return new PagedResult<ReviewResponse>(enriched, result.Page, result.PageSize, result.TotalCount);
    }

    private static string MaskName(string firstName) =>
        string.IsNullOrEmpty(firstName)
            ? "Customer"
            : $"{firstName[0].ToString().ToUpperInvariant()}.";
}
