using Marketplace.Domain.Enums;
namespace Marketplace.Application.Modules.Coupons.DTOs;

public sealed record CreateCouponRequest(
    string Code,
    string? Description,
    CouponDiscountType DiscountType,
    decimal DiscountValue,
    decimal? MinimumOrderAmount,
    decimal? MaximumDiscountAmount,
    int? UsageLimit,
    int PerUserLimit,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyList<Guid>? ProductIds);

public sealed record UpdateCouponRequest(
    string? Description,
    decimal DiscountValue,
    decimal? MinimumOrderAmount,
    decimal? MaximumDiscountAmount,
    int? UsageLimit,
    int PerUserLimit,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool IsActive,
    IReadOnlyList<Guid>? ProductIds);

public sealed record ValidateCouponRequest(string Code, IReadOnlyList<Guid>? ProductIds = null, Guid? SellerId = null);

public sealed record CouponResponse(
    Guid Id,
    Guid? SellerId,
    string? SellerName,
    CouponScope Scope,
    string Code,
    string? Description,
    CouponDiscountType DiscountType,
    decimal DiscountValue,
    decimal? MinimumOrderAmount,
    decimal? MaximumDiscountAmount,
    int? UsageLimit,
    int UsageCount,
    int PerUserLimit,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    CouponStatus Status,
    bool IsActiveNow,
    IReadOnlyList<Guid> ProductIds,
    DateTimeOffset CreatedAt);

public sealed record CouponValidationResponse(
    bool IsValid,
    string Code,
    decimal DiscountAmount,
    string? Message,
    string? DiscountLabel);

public sealed record PublicCouponResponse(string Code, string Description, string DiscountLabel, decimal? MinimumOrderAmount, DateTimeOffset EndsAt);
