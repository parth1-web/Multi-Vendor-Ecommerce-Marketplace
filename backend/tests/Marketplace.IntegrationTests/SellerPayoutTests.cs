using FluentAssertions;
using Marketplace.Domain.Commissions;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Sellers;
using Marketplace.Infrastructure.BackgroundJobs;
using Marketplace.Infrastructure.Persistence;
using Marketplace.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Marketplace.IntegrationTests;

/// <summary>
/// The nightly job that turns accrued commissions into payout statements. It writes to a
/// uniquely indexed reference, so two sellers that looked alike had to be able to coexist,
/// and re-running the job has to be free rather than a second payment.
/// </summary>
/// <remarks>
/// The class shares one database, and a seller is settled at most once per period by design, so
/// each scenario settles on its own day. Without that, whichever test ran first would leave the
/// seller already paid and the rest would have nothing to do.
/// </remarks>
public sealed class SellerPayoutTests : IClassFixture<MarketplaceApiFactory>, IAsyncLifetime
{
    private static readonly DateTimeOffset FirstDay = new(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);

    private readonly MarketplaceApiFactory _factory;
    private MarketplaceTestData _data = null!;

    public SellerPayoutTests(MarketplaceApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _data = await _factory.CreateDatabaseAsync();
        await _data.InitialiseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Two_sellers_whose_ids_share_a_prefix_each_receive_their_own_payout()
    {
        var (periodStart, periodEnd) = Day(0);

        // Seller ids are time ordered, so ids minted in the same instant share their leading hex
        // characters. A reference built from those characters alone is the same string for both
        // sellers, which is what used to break the unique index and leave nobody paid.
        _data.SellerAId.ToString()[..6].Should().Be(_data.SellerBId.ToString()[..6],
            "both sellers are minted at the same instant, which is the shape that used to collide");

        await AccrueAsync(_data.SellerAId, 400m, periodStart);
        await AccrueAsync(_data.SellerBId, 200m, periodStart);

        (await ProcessAsync(periodStart, periodEnd)).Should().Be(2);

        var payouts = await PayoutsInPeriodAsync(periodStart, periodEnd);
        payouts.Should().HaveCount(2);
        payouts.Select(p => p.Reference).Should().OnlyHaveUniqueItems();
        payouts.Select(p => p.SellerId).Should().BeEquivalentTo(new[] { _data.SellerAId, _data.SellerBId });
        payouts.Should().OnlyContain(p => p.Status == PayoutStatus.Pending);

        // 10% of the gross: the marketplace keeps the commission and the seller is owed the rest.
        var techWorld = payouts.Single(p => p.SellerId == _data.SellerAId);
        techWorld.GrossAmount.Should().Be(400m);
        techWorld.CommissionAmount.Should().Be(40m);
        techWorld.NetAmount.Should().Be(360m);
    }

    [Fact]
    public async Task Running_the_job_again_for_the_same_period_pays_nobody_twice()
    {
        var (periodStart, periodEnd) = Day(1);

        await AccrueAsync(_data.SellerAId, 400m, periodStart);
        await AccrueAsync(_data.SellerBId, 200m, periodStart);

        (await ProcessAsync(periodStart, periodEnd)).Should().Be(2);
        (await ProcessAsync(periodStart, periodEnd)).Should().Be(0);
        (await ProcessAsync(periodStart, periodEnd)).Should().Be(0);

        (await PayoutsInPeriodAsync(periodStart, periodEnd)).Should().HaveCount(2);
    }

    [Fact]
    public async Task A_seller_paid_for_one_day_is_paid_again_for_the_next()
    {
        var (firstStart, firstEnd) = Day(2);
        var (nextStart, nextEnd) = Day(3);

        await AccrueAsync(_data.SellerAId, 400m, firstStart);
        (await ProcessAsync(firstStart, firstEnd)).Should().Be(1);

        await AccrueAsync(_data.SellerAId, 150m, nextStart);
        (await ProcessAsync(nextStart, nextEnd)).Should().Be(1,
            "the guard is scoped to the period, so a settled seller is still paid for later days");

        var later = await PayoutsInPeriodAsync(nextStart, nextEnd);
        later.Should().ContainSingle();
        later[0].SellerId.Should().Be(_data.SellerAId);
    }

    [Fact]
    public async Task Accrued_commissions_are_settled_by_the_payout_that_carries_them()
    {
        var (periodStart, periodEnd) = Day(4);

        await AccrueAsync(_data.SellerAId, 400m, periodStart);
        await AccrueAsync(_data.SellerAId, 100m, periodStart);
        await AccrueAsync(_data.SellerBId, 200m, periodStart);

        await ProcessAsync(periodStart, periodEnd);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();

        var commissions = await context.Commissions
            .Where(c => c.CreatedAt >= periodStart && c.CreatedAt < periodEnd)
            .ToListAsync();

        commissions.Should().HaveCount(3);
        commissions.Should().OnlyContain(c => c.Status == CommissionStatus.Paid);
        commissions.Should().OnlyContain(c => c.PayoutId != null);

        var payouts = await PayoutsInPeriodAsync(periodStart, periodEnd);
        var techWorld = payouts.Single(p => p.SellerId == _data.SellerAId);
        techWorld.CommissionCount.Should().Be(2, "two of the three commissions belong to this seller");
        techWorld.GrossAmount.Should().Be(500m);

        // Every commission points at the payout that actually carries its money.
        foreach (var commission in commissions)
        {
            var owner = payouts.Single(p => p.SellerId == commission.SellerId);
            commission.PayoutId.Should().Be(owner.Id);
        }
    }

    private static (DateTimeOffset Start, DateTimeOffset End) Day(int offset) =>
        (FirstDay.AddDays(offset), FirstDay.AddDays(offset + 1));

    private async Task AccrueAsync(Guid sellerId, decimal gross, DateTimeOffset periodStart)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();

        // Mid-period, so the commission falls inside the window the job reads.
        var created = periodStart.AddHours(9);
        var commission = Commission.Create(Guid.NewGuid(), Guid.NewGuid(), sellerId, 10m, gross, "USD", created);
        commission.Accrue(created);

        context.Commissions.Add(commission);
        await context.SaveChangesAsync();
    }

    private async Task<int> ProcessAsync(DateTimeOffset periodStart, DateTimeOffset periodEnd)
    {
        using var scope = _factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<SellerPayoutProcessor>();
        return await processor.ProcessAsync(periodStart, periodEnd);
    }

    private async Task<List<SellerPayout>> PayoutsInPeriodAsync(DateTimeOffset periodStart, DateTimeOffset periodEnd)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        return await context.SellerPayouts
            .Where(p => p.PeriodStart == periodStart && p.PeriodEnd == periodEnd)
            .OrderBy(p => p.SellerId)
            .ToListAsync();
    }
}
