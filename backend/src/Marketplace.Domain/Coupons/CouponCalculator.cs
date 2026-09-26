using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Coupons;

/// <summary>
/// Validates a coupon against a basket and computes the discount. Pure, deterministic
/// and side-effect free so the exact rules can be unit tested without a database.
/// </summary>
public static class CouponCalculator
{
    /// <summary>
    /// Evaluates a coupon. <paramref name="eligibleSubtotal"/> is the basket total after
    /// restricting the lines to the ones the coupon actually applies to (seller scope and
    /// product scope), which is what stops a marketplace-wide coupon leaking a discount
    /// onto a seller who is not participating.
    /// </summary>
    public static CouponValidationResult Validate(
        Coupon? coupon,
        decimal eligibleSubtotal,
        int userUsageCount,
        Guid? couponSellerId,
        Guid? basketSellerId,
        IReadOnlyCollection<Guid>? basketProductIds,
        DateTimeOffset now)
    {
        if (coupon is null)
        {
            return CouponValidationResult.Invalid("The coupon code was not recognised.");
        }

        if (coupon.Status != CouponStatus.Active)
        {
            return CouponValidationResult.Invalid($"This coupon is {coupon.Status.ToString().ToLowerInvariant()}.");
        }

        if (now < coupon.StartsAt)
        {
            return CouponValidationResult.Invalid("This coupon is not active yet.");
        }

        if (now > coupon.EndsAt)
        {
            return CouponValidationResult.Invalid("This coupon has expired.");
        }

        if (coupon.IsExhausted)
        {
            return CouponValidationResult.Invalid("This coupon has reached its usage limit.");
        }

        if (userUsageCount >= coupon.PerUserLimit)
        {
            return CouponValidationResult.Invalid("You have already used this coupon the maximum number of times.");
        }

        if (!coupon.IsGlobal)
        {
            if (coupon.SellerId is null)
            {
                return CouponValidationResult.Invalid("This coupon is not correctly configured.");
            }

            if (basketSellerId is not null && coupon.SellerId != basketSellerId)
            {
                return CouponValidationResult.Invalid("This coupon only applies to products from one store.");
            }
        }

        if (coupon.MinimumOrderAmount is not null && eligibleSubtotal < coupon.MinimumOrderAmount.Value)
        {
            return CouponValidationResult.Invalid(
                $"This coupon requires a minimum order of {coupon.MinimumOrderAmount.Value:0.00}.");
        }

        if (basketProductIds is not null && coupon.ProductIds.Count > 0)
        {
            var anyMatch = coupon.ProductIds.Any(id => basketProductIds.Contains(id));
            if (!anyMatch)
            {
                return CouponValidationResult.Invalid("This coupon does not apply to any product in your cart.");
            }
        }

        var discount = CalculateDiscount(coupon, eligibleSubtotal);
        if (discount <= 0m)
        {
            return CouponValidationResult.Invalid("This coupon produces no discount on the current cart.");
        }

        return CouponValidationResult.Valid(discount);
    }

    /// <summary>
    /// Percentage or fixed discount, capped by <c>MaximumDiscountAmount</c> and by the
    /// eligible subtotal itself.
    /// </summary>
    public static decimal CalculateDiscount(Coupon coupon, decimal eligibleSubtotal)
    {
        ArgumentNullException.ThrowIfNull(coupon);

        if (eligibleSubtotal <= 0m)
        {
            return 0m;
        }

        var raw = coupon.DiscountType == CouponDiscountType.Percentage
            ? eligibleSubtotal * coupon.DiscountValue / 100m
            : coupon.DiscountValue;

        if (coupon.MaximumDiscountAmount is not null && raw > coupon.MaximumDiscountAmount.Value)
        {
            raw = coupon.MaximumDiscountAmount.Value;
        }

        if (raw > eligibleSubtotal)
        {
            raw = eligibleSubtotal;
        }

        return decimal.Round(Math.Max(0m, raw), 2, MidpointRounding.AwayFromZero);
    }
}

/// <summary>Outcome of validating a coupon, including the computed discount on success.</summary>
public readonly record struct CouponValidationResult(bool IsValid, decimal DiscountAmount, string? Reason)
{
    public static CouponValidationResult Valid(decimal discount) => new(true, discount, null);

    public static CouponValidationResult Invalid(string reason) => new(false, 0m, reason);
}
