using FluentAssertions;
using Marketplace.Domain.Commissions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;
using Xunit;

namespace Marketplace.UnitTests.Commissions;

public sealed class CommissionCalculatorTests
{
    [Fact]
    public void Ten_percent_of_ten_thousand_gives_one_thousand_to_the_marketplace()
    {
        var result = CommissionCalculator.Calculate(10_000m, 10m);

        result.CommissionAmount.Should().Be(1_000m);
        result.SellerAmount.Should().Be(9_000m);
    }

    [Fact]
    public void The_split_always_adds_back_to_the_sale_amount()
    {
        var result = CommissionCalculator.Calculate(1_234.56m, 12.5m);

        result.Total.Should().Be(1_234.56m);
    }

    [Fact]
    public void A_zero_rate_gives_everything_to_the_seller()
    {
        var result = CommissionCalculator.Calculate(500m, 0m);

        result.CommissionAmount.Should().Be(0m);
        result.SellerAmount.Should().Be(500m);
    }

    [Fact]
    public void A_full_rate_gives_everything_to_the_marketplace()
    {
        var result = CommissionCalculator.Calculate(500m, 100m);

        result.CommissionAmount.Should().Be(500m);
        result.SellerAmount.Should().Be(0m);
    }

    [Fact]
    public void Rounding_is_midpoint_away_from_zero_and_reproducible()
    {
        var first = CommissionCalculator.Calculate(19.99m, 10m);
        var second = CommissionCalculator.Calculate(19.99m, 10m);

        first.Should().Be(second);
        first.CommissionAmount.Should().Be(2m);
    }

    [Fact]
    public void A_negative_sale_amount_is_rejected()
    {
        var act = () => CommissionCalculator.Calculate(-1m, 10m);

        act.Should().Throw<ValidationException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void A_rate_outside_zero_to_one_hundred_is_rejected(decimal rate)
    {
        var act = () => CommissionCalculator.Calculate(100m, rate);

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void Total_marketplace_revenue_sums_every_line()
    {
        var revenue = CommissionCalculator.TotalMarketplaceRevenue(
        [
            (10_000m, 10m),
            (5_000m, 20m),
            (2_000m, 0m)
        ]);

        revenue.Should().Be(2_000m);
    }

    [Fact]
    public void Commission_snapshots_the_rate_so_a_later_change_cannot_rewrite_history()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var commission = Commission.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10m, 1_000m, "USD", now);

        commission.Rate.Should().Be(10m);
        commission.CommissionAmount.Should().Be(100m);
        commission.SellerAmount.Should().Be(900m);
    }

    [Fact]
    public void A_paid_commission_cannot_be_reversed()
    {
        var now = DateTimeOffset.UtcNow;
        var commission = Commission.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10m, 100m, "USD", now);
        commission.Accrue(now);
        commission.MarkPayable(now);
        commission.MarkPaid(Guid.NewGuid(), now);

        var act = () => commission.Reverse("refund", now);

        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void Reversing_twice_is_a_no_op()
    {
        var now = DateTimeOffset.UtcNow;
        var commission = Commission.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10m, 100m, "USD", now);
        commission.Accrue(now);

        commission.Reverse("refund-1", now);
        var act = () => commission.Reverse("refund-2", now);

        act.Should().NotThrow();
        commission.Status.Should().Be(CommissionStatus.Reversed);
    }
}
