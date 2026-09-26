using Marketplace.Domain.Catalog;
using Marketplace.Domain.Coupons;

namespace Marketplace.Infrastructure.Persistence;

/// <summary>Join entity scoping a coupon to specific products.</summary>
public class CouponProduct
{
    public Guid CouponId { get; set; }

    public Guid ProductId { get; set; }

    public Coupon? Coupon { get; set; }

    public Product? Product { get; set; }
}
