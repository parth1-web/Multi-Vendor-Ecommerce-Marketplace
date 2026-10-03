using Marketplace.Application.Common.Extensions;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Analytics.DTOs;
using Marketplace.Application.Modules.Catalog.Abstractions;
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
  IProductService catalogue,
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

    /// <summary>
    /// Whether the caller may act on the seller identified by <paramref name="sellerId"/>.
    /// </summary>
    /// <remarks>
    /// The whole of object-level authorization for this service, and the reason it exists as one
    /// predicate rather than as a check at each call site: a seller may only ever reach their own
    /// record, and only an administrator reaches anybody else's. The seller identity comes from the
    /// token's <c>sid</c> claim, so a caller cannot widen their own scope by editing a route.
    /// </remarks>
    private bool CanAccess(Guid sellerId) =>
        currentUser.IsAdmin || (currentUser.SellerId is { } ownSellerId && ownSellerId == sellerId);

    public async Task<Result<SellerResponse>> GetByIdAsync(Guid sellerId, CancellationToken cancellationToken = default)
    {
        // Checked before the lookup so an unauthorized caller is refused identically whether or not
        // the seller exists. The message is the one an absent seller gets, so a probe cannot learn
        // that somebody else's account is real.
        if (!CanAccess(sellerId))
        {
            logger.LogWarning(
                "Denied seller read for {SellerId} by user {UserId} (role {Role}, seller claim {ClaimedSellerId}).",
                sellerId, currentUser.UserId, currentUser.Role, currentUser.SellerId);
            return Result<SellerResponse>.Failure("Seller not found.", ResultErrorCodes.NotFound);
        }

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

    /// <summary>
    /// Changes a seller record: business identity, contact details, tax and bank information.
    /// </summary>
    /// <remarks>
    /// The same ownership predicate as the read, because a write path with no ownership check is
    /// how a seller rewrites another seller's tax identity and bank details. Refused before the
    /// row is loaded, so the refusal cannot be used to probe for existence either.
    /// </remarks>
    public async Task<Result<SellerResponse>> UpdateAsync(Guid sellerId, UpdateSellerRequest request, CancellationToken cancellationToken = default)
    {
        if (!CanAccess(sellerId))
        {
            logger.LogWarning(
                "Denied seller update for {SellerId} by user {UserId} (role {Role}, seller claim {ClaimedSellerId}).",
                sellerId, currentUser.UserId, currentUser.Role, currentUser.SellerId);
            return Result<SellerResponse>.Failure("Seller not found.", ResultErrorCodes.NotFound);
        }

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
        await auditService.RecordAsync(AuditAction.SellerProfileUpdated, nameof(Seller), seller.Id, seller.BusinessName,
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

    /// <inheritdoc />
    public async Task<PagedResult<StoreDirectoryEntryResponse>> ListPublicStoresAsync(
        PageRequest page, string? search, CancellationToken cancellationToken = default)
    {
        var query = stores.Query().AsNoTracking().Where(s => s.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Matched without regard to case in the database, because "kathmandu" typed into a
            // search box should find "Kathmandu" rather than nothing.
            var term = search.Trim().ToLowerInvariant();
            var matching = sellers.Query().AsNoTracking()
                .Where(s => s.BusinessName.ToLower().Contains(term))
                .Select(s => s.Id);

            query = query.Where(s => matching.Contains(s.SellerId)
                || s.Name.ToLower().Contains(term)
                || s.Description.ToLower().Contains(term));
        }

        // Best rated first, then busiest: a directory ordered by nothing in particular is a
        // directory nobody can find anything in.
        query = query.OrderByDescending(s => s.RatingAverage).ThenByDescending(s => s.TotalSalesCount);

        return await query.ToPagedResultAsync(page, s => new StoreDirectoryEntryResponse(
            s.SellerId,
            s.Id,
            s.Name,
            s.SlugValue,
            s.LogoUrl,
            s.BannerUrl,
            s.Description,
            s.ProductCount,
            s.RatingAverage,
            s.RatingCount), cancellationToken).ConfigureAwait(false);
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

        // A storefront shows the same cards as the catalogue, so it asks the catalogue service
        // for them rather than projecting a second, thinner copy here. That copy had no stock, no
        // category and no inventory, and a storefront that claims everything is in stock is
        // worse than one that shows nothing.
        var productPage = await catalogue.ListAsync(
            new ProductQuery(page.Page, page.PageSize, null, null, null, store.SellerId, null, null, null, null, null, null, null, ProductSortOption.Popular),
            cancellationToken).ConfigureAwait(false);

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

            // Counted from what the storefront is actually showing, because a denormalised
            // counter drifts the moment anything writes a product without going through the
            // service that maintains it.
            productPage.TotalCount,
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
