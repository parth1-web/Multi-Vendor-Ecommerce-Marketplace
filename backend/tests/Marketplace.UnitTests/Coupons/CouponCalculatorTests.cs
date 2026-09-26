using FluentAssertions;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Marketplace.Domain.Coupons;
using Marketplace.Domain.Enums;
using Xunit;

namespace Marketplace.UnitTests.Coupons;

public sealed class CouponCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static Coupon Percentage(decimal value, decimal? minimum = null, decimal? maximum = null, CouponScope scope = CouponScope.Global, Guid? sellerId = null) =>
        Coupon.Create(sellerId, scope, "TEST10", "test", CouponDiscountType.Percentage, value, minimum, maximum, null, 5,
            Now.AddDays(-1), Now.AddDays(30), Now);

    [Fact]
    public void CalculateDiscount_applies_percentage_of_eligible_subtotal()
    {
        var discount = CouponCalculator.CalculateDiscount(Percentage(10m), 200m);

        discount.Should().Be(20m);
    }

    [Fact]
    public void CalculateDiscount_applies_fixed_amount_regardless_of_subtotal()
    {
        var coupon = Coupon.Create(null, CouponScope.Global, "FLAT15", "test", CouponDiscountType.FixedAmount, 15m, null, null, null, 1, Now.AddDays(-1), Now.AddDays(10), Now);

        CouponCalculator.CalculateDiscount(coupon, 250m).Should().Be(15m);
    }

    [Fact]
    public void CalculateDiscount_never_exceeds_the_maximum_discount()
    {
        var discount = CouponCalculator.CalculateDiscount(Percentage(50m, maximum: 30m), 1000m);

        discount.Should().Be(30m);
    }

    [Fact]
    public void CalculateDiscount_never_exceeds_the_eligible_subtotal()
    {
        var coupon = Coupon.Create(null, CouponScope.Global, "BIGFIXED", "test", CouponDiscountType.FixedAmount, 50m, null, null, null, 1,
            Now.AddDays(-1), Now.AddDays(10), Now);

        CouponCalculator.CalculateDiscount(coupon, 12m).Should().Be(12m);
    }

    [Fact]
    public void CalculateDiscount_returns_zero_for_a_zero_subtotal()
    {
        CouponCalculator.CalculateDiscount(Percentage(10m), 0m).Should().Be(0m);
    }

    [Fact]
    public void Validate_accepts_an_active_coupon_above_the_minimum()
    {
        var result = CouponCalculator.Validate(Percentage(10m, minimum: 50m), 120m, 0, null, null, null, Now);

        result.IsValid.Should().BeTrue();
        result.DiscountAmount.Should().Be(12m);
    }

    [Fact]
    public void Validate_rejects_a_basket_below_the_minimum_and_explains_why()
    {
        var result = CouponCalculator.Validate(Percentage(10m, minimum: 100m), 40m, 0, null, null, null, Now);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("minimum order");
    }

    [Fact]
    public void Validate_rejects_a_coupon_that_has_not_started()
    {
        var coupon = Coupon.Create(null, CouponScope.Global, "FUTURE", "test", CouponDiscountType.Percentage, 10m, null, null, null, 1,
            Now.AddDays(5), Now.AddDays(30), Now);

        var result = CouponCalculator.Validate(coupon, 200m, 0, null, null, null, Now);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("not active yet");
    }

    [Fact]
    public void Validate_rejects_an_expired_coupon()
    {
        var result = CouponCalculator.Validate(Percentage(10m), 200m, 0, null, null, null, Now.AddDays(31));

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("expired");
    }

    [Fact]
    public void Validate_rejects_once_the_per_user_limit_is_reached()
    {
        var result = CouponCalculator.Validate(Percentage(10m), 200m, 5, null, null, null, Now);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("already used");
    }

    [Fact]
    public void Validate_rejects_a_seller_coupon_for_another_sellers_basket()
    {
        var sellerId = Guid.NewGuid();
        var coupon = Percentage(10m, scope: CouponScope.Seller, sellerId: sellerId);

        var result = CouponCalculator.Validate(coupon, 200m, 0, sellerId, Guid.NewGuid(), null, Now);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("one store");
    }

    [Fact]
    public void Validate_rejects_a_coupon_scoped_to_products_that_are_not_in_the_basket()
    {
        var coupon = Percentage(10m);
        coupon.RestrictToProducts([Guid.NewGuid()], Now);

        var result = CouponCalculator.Validate(coupon, 200m, 0, null, null, [Guid.NewGuid(), Guid.NewGuid()], Now);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("does not apply to any product");
    }

    [Fact]
    public void Validate_rejects_a_null_coupon()
    {
        var result = CouponCalculator.Validate(null, 200m, 0, null, null, null, Now);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("not recognised");
    }

    [Fact]
    public void RecordUsage_stops_at_the_global_limit_and_marks_the_coupon_exhausted()
    {
        var coupon = Coupon.Create(null, CouponScope.Global, "LIMITED", "test", CouponDiscountType.Percentage, 10m, null, null, 2, 1,
            Now.AddDays(-1), Now.AddDays(10), Now);

        coupon.RecordUsage(Now);
        coupon.RecordUsage(Now);

        coupon.UsageCount.Should().Be(2);
        coupon.IsExhausted.Should().BeTrue();

        var act = () => coupon.RecordUsage(Now);
        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void Create_rejects_a_percentage_above_one_hundred()
    {
        var act = () => Coupon.Create(null, CouponScope.Global, "TOOMUCH", "test", CouponDiscountType.Percentage, 150m, null, null, null, 1, Now, Now.AddDays(5), Now);

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void Create_rejects_a_window_that_ends_before_it_starts()
    {
        var act = () => Coupon.Create(null, CouponScope.Global, "BACKWARDS", "test", CouponDiscountType.Percentage, 10m, null, null, null, 1, Now, Now.AddDays(-1), Now);

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void Create_rejects_a_seller_coupon_without_a_seller()
    {
        var act = () => Coupon.Create(null, CouponScope.Seller, "NOSELLER", "test", CouponDiscountType.Percentage, 10m, null, null, null, 1, Now, Now.AddDays(5), Now);

        act.Should().Throw<ValidationException>();
    }
}
