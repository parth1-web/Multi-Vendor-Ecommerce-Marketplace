using Marketplace.Domain.Common;

namespace Marketplace.Domain.Coupons;

/// <summary>Records that a specific user consumed a coupon, enforcing the per-user limit.</summary>
public class CouponUsage : Entity
{
    private CouponUsage()
    {
    }

    private CouponUsage(Guid id, Guid couponId, Guid userId, Guid orderId, decimal discountAmount, DateTimeOffset now)
        : base(id)
    {
        CouponId = couponId;
        UserId = userId;
        OrderId = orderId;
        DiscountAmount = discountAmount;
        UsedAt = now;
    }

    public Guid CouponId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid OrderId { get; private set; }

    public decimal DiscountAmount { get; private set; }

    public DateTimeOffset UsedAt { get; private set; }

    public static CouponUsage Create(Guid couponId, Guid userId, Guid orderId, decimal discountAmount, DateTimeOffset now)
    {
        Guard.NotEmpty(couponId, nameof(couponId));
        Guard.NotEmpty(userId, nameof(userId));
        Guard.NotEmpty(orderId, nameof(orderId));

        return new CouponUsage(SequentialGuid.New(now), couponId, userId, orderId, decimal.Round(discountAmount, 2), now);
    }
}
