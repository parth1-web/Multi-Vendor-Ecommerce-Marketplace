using Marketplace.Application.Common.Extensions;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Coupons.Abstractions;
using Marketplace.Application.Modules.Coupons.DTOs;
using Marketplace.Domain.Coupons;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Sellers;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Modules.Coupons.Services;

/// <summary>
/// Coupon management. Admin-created coupons are global; a seller may only ever see and
/// manage their own. Validation explains precisely why a code was refused so the UI can
/// show an actionable message.
/// </summary>
public sealed class CouponService(
    IRepository<Coupon> coupons,
    IRepository<CouponUsage> usages,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    IClock clock,
    IAuditService auditService) : ICouponService
{
    public async Task<PagedResult<CouponResponse>> ListAsync(CouponListQuery query, CancellationToken cancellationToken = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var source = coupons.Query().AsNoTracking();

        if (!currentUser.IsAdmin)
        {
            if (currentUser.SellerId is not { } sellerId)
            {
                return PagedResult<CouponResponse>.Empty(page.Page, page.PageSize);
            }

            source = source.Where(c => c.SellerId == sellerId);
        }
        else if (query.MineOnly == true && currentUser.SellerId is { } ownSeller)
        {
            source = source.Where(c => c.SellerId == ownSeller);
        }

        if (query.Status is { } status)
        {
            source = source.Where(c => c.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim().ToUpperInvariant()}%";
            source = source.Where(c => EF.Functions.Like(c.Code, term));
        }

        return await source
            .OrderByDescending(c => c.CreatedAt)
            .ToPagedResultAsync(page, c => new CouponResponse(
                c.Id, c.SellerId, string.Empty, c.Scope, c.Code, c.Description, c.DiscountType, c.DiscountValue,
                c.MinimumOrderAmount, c.MaximumDiscountAmount, c.UsageLimit, c.UsageCount, c.PerUserLimit,
                c.StartsAt, c.EndsAt, c.Status, c.IsActiveOn(clock.UtcNow), c.ProductIds.ToList(), c.CreatedAt), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PublicCouponResponse>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        var rows = await coupons.Query().AsNoTracking()
            .Where(c => c.Status == CouponStatus.Active && c.StartsAt <= now && c.EndsAt >= now)
            .OrderBy(c => c.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Where(c => c.UsageLimit is null || c.UsageCount < c.UsageLimit)
            .Select(c => new PublicCouponResponse(
                c.Code,
                c.Description,
                c.DiscountType == CouponDiscountType.Percentage ? $"{c.DiscountValue:0.##}% off" : $"{c.DiscountValue:0.00} off",
                c.MinimumOrderAmount,
                c.EndsAt))
            .ToList();
    }

    public async Task<Result<CouponResponse>> CreateAsync(CreateCouponRequest request, CancellationToken cancellationToken = default)
    {
        var scope = CouponScope.Global;
        Guid? sellerId = null;

        if (!currentUser.IsAdmin)
        {
            if (currentUser.SellerId is not { } ownSeller)
            {
                return Result<CouponResponse>.Failure("Only sellers or administrators can create coupons.");
            }

            scope = CouponScope.Seller;
            sellerId = ownSeller;
        }

        var code = request.Code.Trim().ToUpperInvariant();
        if (await coupons.AnyAsync(c => c.Code == code, cancellationToken).ConfigureAwait(false))
        {
            return Result<CouponResponse>.Failure("A coupon with this code already exists.");
        }

        var coupon = Coupon.Create(
            sellerId, scope, code, request.Description ?? string.Empty, request.DiscountType, request.DiscountValue,
            request.MinimumOrderAmount, request.MaximumDiscountAmount, request.UsageLimit,
            request.PerUserLimit, request.StartsAt, request.EndsAt, clock.UtcNow);

        if (request.ProductIds is { Count: > 0 })
        {
            coupon.RestrictToProducts(request.ProductIds, clock.UtcNow);
        }

        await coupons.AddAsync(coupon, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.CouponCreated, nameof(Coupon), coupon.Id, coupon.Code,
            new { Code = coupon.Code, Type = coupon.DiscountType.ToString(), Value = coupon.DiscountValue }, cancellationToken).ConfigureAwait(false);

        return Result<CouponResponse>.Success(Map(coupon, null));
    }

    public async Task<Result<CouponResponse>> UpdateAsync(Guid id, UpdateCouponRequest request, CancellationToken cancellationToken = default)
    {
        var coupon = await coupons.Query().FirstOrDefaultAsync(c => c.Id == id, cancellationToken).ConfigureAwait(false);
        if (coupon is null)
        {
            return Result<CouponResponse>.Failure("Coupon not found.", ResultErrorCodes.NotFound);
        }

        if (!currentUser.IsAdmin)
        {
            if (currentUser.SellerId is not { } sellerId || coupon.SellerId != sellerId)
            {
                return Result<CouponResponse>.Failure("You can only manage your own coupons.");
            }
        }

        coupon.Update(
            request.Description ?? string.Empty, request.DiscountValue, request.MinimumOrderAmount,
            request.MaximumDiscountAmount, request.UsageLimit, request.PerUserLimit,
            request.StartsAt, request.EndsAt, clock.UtcNow);

        coupon.SetStatus(request.IsActive ? CouponStatus.Active : CouponStatus.Paused, clock.UtcNow);

        if (request.ProductIds is not null)
        {
            coupon.RestrictToProducts(request.ProductIds, clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.CouponUpdated, nameof(Coupon), coupon.Id, coupon.Code,
            new { Value = coupon.DiscountValue, Status = coupon.Status.ToString() }, cancellationToken).ConfigureAwait(false);

        return Result<CouponResponse>.Success(Map(coupon, null));
    }

    /// <summary>
    /// Stops a coupon: it stops applying, and the row stays.
    /// </summary>
    /// <remarks>
    /// Not a removal, and deliberately so. A code that has been used is part of the record of
    /// what was discounted and why, and a code that has not is a code somebody may have written
    /// down and will try again next week. Either way the honest operation is "stopped" rather than
    /// "gone", so the audit entry says that too. Editing a stopped coupon sets it running again.
    /// </remarks>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var coupon = await coupons.Query().FirstOrDefaultAsync(c => c.Id == id, cancellationToken).ConfigureAwait(false);
        if (coupon is null)
        {
            return Result.Failure("Coupon not found.", ResultErrorCodes.NotFound);
        }

        if (!currentUser.IsAdmin && (currentUser.SellerId is not { } sellerId || coupon.SellerId != sellerId))
        {
            return Result.Failure("You can only manage your own coupons.");
        }

        coupon.SetStatus(CouponStatus.Paused, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.CouponDeactivated, nameof(Coupon), coupon.Id, coupon.Code, null, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    public async Task<Result<CouponValidationResponse>> ValidateAsync(ValidateCouponRequest request, decimal? basketSubtotal, CancellationToken cancellationToken = default)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var coupon = await coupons.Query().AsNoTracking().FirstOrDefaultAsync(c => c.Code == code, cancellationToken).ConfigureAwait(false);

        if (coupon is null)
        {
            return Result<CouponValidationResponse>.Success(new CouponValidationResponse(false, code, 0m, "This coupon code was not recognised.", null));
        }

        var usageCount = currentUser.IsAuthenticated
            ? await usages.Query().AsNoTracking().CountAsync(u => u.CouponId == coupon.Id && u.UserId == currentUser.UserId, cancellationToken).ConfigureAwait(false)
            : 0;

        var subtotal = basketSubtotal ?? 0m;
        var productIds = request.ProductIds?.ToArray();

        var result = CouponCalculator.Validate(
            coupon,
            subtotal,
            usageCount,
            coupon.SellerId,
            request.SellerId,
            productIds,
            clock.UtcNow);

        var label = coupon.DiscountType == CouponDiscountType.Percentage
            ? $"{coupon.DiscountValue:0.##}% off"
            : $"{coupon.DiscountValue:0.00} off";

        return Result<CouponValidationResponse>.Success(new CouponValidationResponse(
            result.IsValid,
            coupon.Code,
            result.DiscountAmount,
            result.Reason,
            result.IsValid ? label : null));
    }

    private CouponResponse Map(Coupon c, string? sellerName) => new(
        c.Id, c.SellerId, sellerName, c.Scope, c.Code, c.Description, c.DiscountType, c.DiscountValue,
        c.MinimumOrderAmount, c.MaximumDiscountAmount, c.UsageLimit, c.UsageCount, c.PerUserLimit,
        c.StartsAt, c.EndsAt, c.Status, c.IsActiveOn(clock.UtcNow), c.ProductIds.ToList(), c.CreatedAt);
}
