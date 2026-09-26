using FluentAssertions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Orders;
using Xunit;

namespace Marketplace.UnitTests.Orders;

public sealed class OrderPricingCalculatorTests
{
    private static readonly Guid SellerA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SellerB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ProductA = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ProductB = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void Subtotal_is_the_sum_of_every_line()
    {
        var totals = OrderPricingCalculator.Calculate(
            [new PricedLine(ProductA, Guid.NewGuid(), SellerA, 100m, 2)],
            null, 9.99m, 150m, 0m);

        totals.Subtotal.Should().Be(200m);
    }

    [Fact]
    public void Shipping_is_waived_once_the_basket_reaches_the_free_threshold()
    {
        var totals = OrderPricingCalculator.Calculate(
            [new PricedLine(ProductA, Guid.NewGuid(), SellerA, 200m, 1)],
            null, 9.99m, 150m, 0m);

        totals.ShippingAmount.Should().Be(0m);
        totals.TotalAmount.Should().Be(200m);
    }

    [Fact]
    public void Shipping_is_charged_per_seller_below_the_threshold()
    {
        var totals = OrderPricingCalculator.Calculate(
        [
            new PricedLine(ProductA, Guid.NewGuid(), SellerA, 50m, 1),
            new PricedLine(ProductB, Guid.NewGuid(), SellerB, 40m, 1)
        ],
            null, 9.99m, 150m, 0m);

        totals.ShippingAmount.Should().Be(19.98m);
        totals.TotalAmount.Should().Be(109.98m);
    }

    [Fact]
    public void A_discount_reduces_the_total_and_is_tracked_per_seller()
    {
        var totals = OrderPricingCalculator.Calculate(
        [
            new PricedLine(ProductA, Guid.NewGuid(), SellerA, 100m, 1),
            new PricedLine(ProductB, Guid.NewGuid(), SellerB, 100m, 1)
        ],
            new Dictionary<Guid, decimal> { [SellerA] = 20m },
            9.99m, 150m, 0m);

        totals.DiscountAmount.Should().Be(20m);
        totals.DiscountBySeller[SellerA].Should().Be(20m);
        totals.DiscountBySeller.Should().NotContainKey(SellerB);
    }

    [Fact]
    public void A_discount_larger_than_the_subtotal_is_capped_and_the_zero_total_is_refused()
    {
        var act = () => OrderPricingCalculator.Calculate(
            [new PricedLine(ProductA, Guid.NewGuid(), SellerA, 50m, 1)],
            new Dictionary<Guid, decimal> { [SellerA] = 500m },
            0m, 0m, 0m);

        act.Should().Throw<Domain.Common.BusinessRuleException>();
    }

    [Fact]
    public void Tax_is_applied_after_the_discount()
    {
        var totals = OrderPricingCalculator.Calculate(
            [new PricedLine(ProductA, Guid.NewGuid(), SellerA, 200m, 1)],
            null, 9.99m, 1000m, 10m);

        totals.TaxAmount.Should().Be(20m);
        totals.ShippingAmount.Should().Be(9.99m);
        totals.TotalAmount.Should().Be(229.99m);
    }

    [Fact]
    public void An_empty_basket_is_rejected()
    {
        var act = () => OrderPricingCalculator.Calculate([], null, 9.99m, 150m, 0m);

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void A_negative_shipping_cost_is_rejected()
    {
        var act = () => OrderPricingCalculator.Calculate(
            [new PricedLine(ProductA, Guid.NewGuid(), SellerA, 10m, 1)], null, -1m, 150m, 0m);

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void A_tax_rate_above_one_hundred_is_rejected()
    {
        var act = () => OrderPricingCalculator.Calculate(
            [new PricedLine(ProductA, Guid.NewGuid(), SellerA, 10m, 1)], null, 9.99m, 150m, 101m);

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void The_seller_breakdown_always_sums_back_to_the_subtotal()
    {
        var totals = OrderPricingCalculator.Calculate(
        [
            new PricedLine(ProductA, Guid.NewGuid(), SellerA, 33.33m, 3),
            new PricedLine(ProductB, Guid.NewGuid(), SellerB, 17.77m, 2)
        ],
            null, 9.99m, 1000m, 0m);

        totals.SellerBreakdown.Sum(s => s.Subtotal).Should().Be(totals.Subtotal);
    }

    [Fact]
    public void Allocating_a_discount_distributes_it_across_sellers_exactly()
    {
        var breakdown = new[]
        {
            new SellerSubtotal(SellerA, 100m, 1),
            new SellerSubtotal(SellerB, 50m, 1)
        };

        var allocation = OrderPricingCalculator.AllocateDiscount(breakdown, 15m);

        allocation.Values.Sum().Should().Be(15m);
        allocation[SellerA].Should().Be(10m);
        allocation[SellerB].Should().Be(5m);
    }

    [Fact]
    public void Allocating_a_discount_with_awkward_numbers_still_sums_exactly()
    {
        var breakdown = new[]
        {
            new SellerSubtotal(SellerA, 33.33m, 1),
            new SellerSubtotal(SellerB, 33.33m, 1),
            new SellerSubtotal(Guid.NewGuid(), 33.34m, 1)
        };

        var allocation = OrderPricingCalculator.AllocateDiscount(breakdown, 10m);

        decimal.Round(allocation.Values.Sum(), 2).Should().Be(10m);
    }

    [Fact]
    public void Allocating_a_zero_discount_produces_no_entries()
    {
        var breakdown = new[] { new SellerSubtotal(SellerA, 100m, 1) };

        OrderPricingCalculator.AllocateDiscount(breakdown, 0m).Should().BeEmpty();
    }
}
