using Marketplace.Application.Common.Extensions;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Analytics.DTOs;
using Marketplace.Application.Modules.Catalog.DTOs;
using Marketplace.Application.Modules.Auth.Abstractions;
using Marketplace.Application.Modules.Sellers.Abstractions;
using Marketplace.Application.Modules.Sellers.DTOs;
using Marketplace.Domain.Commissions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Events;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Identity;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Sellers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marketplace.Application.Modules.Sellers.Services;

/// <summary>
/// Seller lifecycle and store profile management.
///
/// Every read or write is scoped with an explicit seller predicate derived from the
/// authenticated token — never from a request body — which is the core anti-IDOR control.
/// </summary>
public sealed class SellerService(
    IRepository<Seller> sellers,
    IRepository<SellerStore> stores,
    IRepository<User> users,
    IRepository<SellerOrder> sellerOrders,
    IRepository<Commission> commissions,
    IRepository<Domain.Catalog.Product> products,
    ICurrentUser currentUser,
    ICacheService cache,
    IUnitOfWork unitOfWork,
    IClock clock,
    IAuditService auditService,
    INotificationService notificationService,
    IRealtimeNotifier realtime,
    ILogger<SellerService> logger) : ISellerService
{
    public async Task<Result<SellerResponse>> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.SellerId is not { } sellerId)
        {
            return Result<SellerResponse>.Failure("The current account is not a seller.");
        }

        return await GetByIdAsync(sellerId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<SellerResponse>> GetByIdAsync(Guid sellerId, CancellationToken cancellationToken = default)
    {
        var seller = await sellers.GetByIdAsync(sellerId, cancellationToken).ConfigureAwait(false);
        if (seller is null)
        {
            return Result<SellerResponse>.Failure("Seller not found.", ResultErrorCodes.NotFound);
        }

        return Result<SellerResponse>.Success(await MapAsync(seller, cancellationToken).ConfigureAwait(false));
    }

    public async Task<PagedResult<SellerListItemResponse>> ListAsync(SellerListQuery query, CancellationToken cancellationToken = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);

        var source = sellers.Query().AsNoTracking();

        if (query.Status is { } status)
        {
            source = source.Where(s => s.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            source = source.Where(s => EF.Functions.Like(s.BusinessName, term));
        }

        var projected = source.Select(s => new SellerListItemResponse(
            s.Id,
            s.UserId,
            s.BusinessName,
            users.Query()
                .Where(u => u.Id == s.UserId)
                .Select(u => u.FirstName + " " + u.LastName)
                .FirstOrDefault() ?? string.Empty,
            users.Query()
                .Where(u => u.Id == s.UserId)
                .Select(u => u.Email)
                .FirstOrDefault() ?? string.Empty,
            s.Status,
            s.DefaultCommissionRate,
            products.Query().Count(p => p.SellerId == s.Id && !p.IsDeleted),
            sellerOrders.Query().Count(so => so.SellerId == s.Id),
            sellerOrders.Query()
                .Where(so => so.SellerId == s.Id && so.Status != SellerOrderStatus.Cancelled)
                .Sum(so => (decimal?)so.Subtotal) ?? 0m,
            stores.Query().Where(st => st.SellerId == s.Id).Select(st => st.SlugValue).FirstOrDefault(),
            s.AppliedAt,
            s.ApprovedAt));

        return await projected.ToPagedResultAsync(page, x => x, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<SellerResponse>> UpdateAsync(Guid sellerId, UpdateSellerRequest request, CancellationToken cancellationToken = default)
    {
        var seller = await sellers.GetByIdAsync(sellerId, cancellationToken).ConfigureAwait(false);
        if (seller is null)
        {
            return Result<SellerResponse>.Failure("Seller not found.", ResultErrorCodes.NotFound);
        }

        seller.UpdateProfile(
            request.BusinessName,
            request.LegalName,
            request.PhoneNumber,
            request.Address,
            request.TaxIdentityNumber,
            request.BankAccountName,
            request.BankAccountNumber,
            clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.SellerApproved, nameof(Seller), seller.Id, seller.BusinessName,
            new { request.BusinessName, request.LegalName }, cancellationToken).ConfigureAwait(false);
        await InvalidateStoreCacheAsync(sellerId, cancellationToken).ConfigureAwait(false);

        return Result<SellerResponse>.Success(await MapAsync(seller, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<SellerResponse>> ChangeStatusAsync(Guid sellerId, UpdateSellerStatusRequest request, Guid adminUserId, CancellationToken cancellationToken = default)
    {
        var seller = await sellers.GetByIdAsync(sellerId, cancellationToken).ConfigureAwait(false);
        if (seller is null)
        {
            return Result<SellerResponse>.Failure("Seller not found.", ResultErrorCodes.NotFound);
        }

        var previous = seller.Status;
        var now = clock.UtcNow;

        switch (request.Status)
        {
            case SellerStatus.Active when previous == SellerStatus.Suspended:
                seller.Resume(now);
                break;
            case SellerStatus.Active:
                seller.Approve(request.CommissionRate ?? seller.DefaultCommissionRate, now);
                break;
            case SellerStatus.Rejected:
                seller.Reject(request.Reason ?? "Application did not meet the marketplace criteria.", now);
                break;
            case SellerStatus.Suspended:
                seller.Suspend(request.Reason ?? "Suspended by an administrator.", now);
                break;
            case SellerStatus.Pending:
                throw new BusinessRuleException("A seller cannot be moved back to Pending.");
        }

        seller.AddDomainEvent(new SellerStatusChangedEvent(seller.Id, seller.UserId, previous, seller.Status, request.Reason, now));

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var action = request.Status switch
        {
            SellerStatus.Active => previous == SellerStatus.Suspended ? AuditAction.SellerResumed : AuditAction.SellerApproved,
            SellerStatus.Rejected => AuditAction.SellerRejected,
            _ => AuditAction.SellerSuspended
        };

        await auditService.RecordAsync(action, nameof(Seller), seller.Id, seller.BusinessName,
            new { From = previous.ToString(), To = seller.Status.ToString(), Reason = request.Reason }, cancellationToken).ConfigureAwait(false);

        await NotifyStatusAsync(seller, previous, request.Reason, cancellationToken).ConfigureAwait(false);
        await realtime.SellerStatusChangedAsync(seller.Id, seller.Status.ToString(), cancellationToken).ConfigureAwait(false);
        await InvalidateStoreCacheAsync(sellerId, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Seller {SellerId} status changed from {Previous} to {Current}", sellerId, previous, seller.Status);

        return Result<SellerResponse>.Success(await MapAsync(seller, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<StoreProfileResponse>> GetStoreBySlugAsync(string slug, PageRequest page, CancellationToken cancellationToken = default)
    {
        var store = await stores.Query()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SlugValue == slug.ToLowerInvariant(), cancellationToken)
            .ConfigureAwait(false);

        if (store is null || !store.IsActive)
        {
            return Result<StoreProfileResponse>.Failure("Store not found.", ResultErrorCodes.NotFound);
        }

        var seller = await sellers.GetByIdAsync(store.SellerId, cancellationToken).ConfigureAwait(false);
        if (seller is null || !seller.IsActive)
        {
            return Result<StoreProfileResponse>.Failure("Store not found.", ResultErrorCodes.NotFound);
        }

        var productPage = await products.Query()
            .AsNoTracking()
            .Where(p => p.SellerId == store.SellerId && p.Status == ProductStatus.Published && !p.IsDeleted)
            .OrderByDescending(p => p.SoldCount)
            .ThenByDescending(p => p.CreatedAt)
            .ToPagedResultAsync(page, p => new ProductSummaryResponse(
                p.Id,
                p.Name,
                p.SlugValue,
                p.ShortDescription,
                p.BasePrice,
                p.CompareAtPrice,
                p.DiscountPercentage,
                p.Images.FirstOrDefault(i => i.IsPrimary)?.Url ?? p.Images.FirstOrDefault()?.Url,
                p.Images.FirstOrDefault(i => i.IsPrimary)?.AltText,
                p.SellerId,
                seller.BusinessName,
                store.Name,
                store.SlugValue,
                p.CategoryId,
                string.Empty,
                string.Empty,
                p.RatingAverage,
                p.RatingCount,
                true,
                0,
                p.IsFeatured,
                false,
                p.SoldCount,
                p.CreatedAt), cancellationToken).ConfigureAwait(false);

        return Result<StoreProfileResponse>.Success(new StoreProfileResponse(
            store.SellerId,
            store.Id,
            store.Name,
            store.SlugValue,
            store.Description,
            store.LogoUrl,
            store.BannerUrl,
            store.SupportEmail,
            store.SupportPhone,
            store.ReturnPolicy,
            store.ShippingPolicy,
            store.FoundedYear,
            store.RatingAverage,
            store.RatingCount,
            store.ProductCount,
            store.TotalSalesCount,
            store.IsActive,
            store.CreatedAt,
            productPage));
    }

    public async Task<Result<StoreProfileResponse>> GetOwnStoreAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.SellerId is not { } sellerId)
        {
            return Result<StoreProfileResponse>.Failure("The current account is not a seller.");
        }

        var store = await stores.Query().AsNoTracking().FirstOrDefaultAsync(s => s.SellerId == sellerId, cancellationToken).ConfigureAwait(false);
        if (store is null)
        {
            return Result<StoreProfileResponse>.Failure("Store not found.", ResultErrorCodes.NotFound);
        }

        var result = await GetStoreBySlugAsync(store.SlugValue, new PageRequest(1, 24), cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? result : Result<StoreProfileResponse>.Failure("Store not found.", ResultErrorCodes.NotFound);
    }

    public async Task<Result<StoreProfileResponse>> UpdateOwnStoreAsync(UpdateStoreRequest request, CancellationToken cancellationToken = default)
    {
        if (currentUser.SellerId is not { } sellerId)
        {
            return Result<StoreProfileResponse>.Failure("The current account is not a seller.");
        }

        var store = await stores.Query().FirstOrDefaultAsync(s => s.SellerId == sellerId, cancellationToken).ConfigureAwait(false);
        if (store is null)
        {
            return Result<StoreProfileResponse>.Failure("Store not found.", ResultErrorCodes.NotFound);
        }

        store.UpdateProfile(
            request.Name,
            request.Description,
            request.LogoUrl,
            request.BannerUrl,
            request.SupportEmail,
            request.SupportPhone,
            request.ReturnPolicy,
            request.ShippingPolicy,
            request.FoundedYear,
            clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.StoreUpdated, nameof(SellerStore), store.Id, store.Name,
            new { request.Name }, cancellationToken).ConfigureAwait(false);
        await InvalidateStoreCacheAsync(sellerId, cancellationToken).ConfigureAwait(false);

        return await GetOwnStoreAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task NotifyStatusAsync(Seller seller, SellerStatus previous, string? reason, CancellationToken cancellationToken)
    {
        var (type, title, body) = seller.Status switch
        {
            SellerStatus.Active when previous == SellerStatus.Suspended => (NotificationType.SellerApproved, "Your store is active again", "Your seller account has been resumed. You can list products and fulfil orders again."),
            SellerStatus.Active => (NotificationType.SellerApproved, "Your seller application is approved", "You can now list products and start selling on the marketplace."),
            SellerStatus.Rejected => (NotificationType.SellerRejected, "Your seller application was declined", reason ?? "Please review the marketplace seller criteria and reapply."),
            SellerStatus.Suspended => (NotificationType.SellerSuspended, "Your seller account is suspended", reason ?? "Your products are delisted until the suspension is lifted."),
            _ => (NotificationType.SystemNotification, "Seller account updated", "Your seller account details changed.")
        };

        await notificationService.NotifySellerAsync(seller.UserId, type, title, body, "/seller", cancellationToken).ConfigureAwait(false);
    }

    private async Task InvalidateStoreCacheAsync(Guid sellerId, CancellationToken cancellationToken)
    {
        await cache.RemoveByTagAsync(CacheKeys.StoreProfileTag, cancellationToken).ConfigureAwait(false);
        await cache.RemoveByTagAsync(CacheKeys.StoreTag(sellerId), cancellationToken).ConfigureAwait(false);
        await cache.RemoveByTagAsync(CacheKeys.CatalogTag, cancellationToken).ConfigureAwait(false);
        await cache.RemoveAsync(CacheKeys.Home(), cancellationToken).ConfigureAwait(false);
    }

    private async Task<SellerResponse> MapAsync(Seller seller, CancellationToken cancellationToken)
    {
        var store = await stores.Query().AsNoTracking()
            .FirstOrDefaultAsync(s => s.SellerId == seller.Id, cancellationToken)
            .ConfigureAwait(false);

        var productCount = await products.Query().AsNoTracking()
            .CountAsync(p => p.SellerId == seller.Id && !p.IsDeleted, cancellationToken)
            .ConfigureAwait(false);

        var sellerOrderQuery = sellerOrders.Query().AsNoTracking().Where(s => s.SellerId == seller.Id);

        var orderCount = await sellerOrderQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        var totalRevenue = await sellerOrderQuery
            .Where(s => s.Status != SellerOrderStatus.Cancelled)
            .SumAsync(s => (decimal?)s.Subtotal, cancellationToken)
            .ConfigureAwait(false) ?? 0m;

        var pendingEarnings = await commissions.Query().AsNoTracking()
            .Where(c => c.SellerId == seller.Id && c.Status == CommissionStatus.Accrued)
            .SumAsync(c => (decimal?)c.SellerAmount, cancellationToken)
            .ConfigureAwait(false) ?? 0m;

        var paidEarnings = await commissions.Query().AsNoTracking()
            .Where(c => c.SellerId == seller.Id && c.Status == CommissionStatus.Paid)
            .SumAsync(c => (decimal?)c.SellerAmount, cancellationToken)
            .ConfigureAwait(false) ?? 0m;

        var commissionPaid = await commissions.Query().AsNoTracking()
            .Where(c => c.SellerId == seller.Id)
            .SumAsync(c => (decimal?)c.CommissionAmount, cancellationToken)
            .ConfigureAwait(false) ?? 0m;

        return new SellerResponse(
            seller.Id,
            seller.UserId,
            seller.Status,
            seller.BusinessName,
            seller.LegalName,
            seller.PhoneNumber,
            seller.Address,
            seller.TaxIdentityNumber,
            seller.BankAccountName,
            seller.BankAccountNumber,
            seller.DefaultCommissionRate,
            seller.RejectionReason,
            seller.SuspensionReason,
            seller.AppliedAt,
            seller.ApprovedAt,
            productCount,
            orderCount,
            decimal.Round(totalRevenue, 2),
            decimal.Round(pendingEarnings, 2),
            decimal.Round(paidEarnings, 2),
            decimal.Round(commissionPaid, 2),
            store?.Name,
            store?.SlugValue,
            store?.RatingAverage ?? 0m,
            store?.RatingCount ?? 0);
    }
}
