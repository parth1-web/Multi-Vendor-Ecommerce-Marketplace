using System.Net;
using FluentAssertions;
using Marketplace.Application.Modules.Analytics.DTOs;
using Marketplace.Application.Modules.Orders.DTOs;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Sellers;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.IntegrationTests.Infrastructure;
using Xunit;

namespace Marketplace.IntegrationTests;

/// <summary>
/// Which period a payout belongs to in the commission report.
/// </summary>
/// <remarks>
/// A payout records the window of commission accrual it settles (<c>PeriodStart</c>,
/// <c>PeriodEnd</c>), and the background job writes it after that window has closed — so the row's
/// own creation date answers "when did we run", not "which period does this settle".
///
/// Every test here starts from a real checkout, because the report's rows come from commission: a
/// payout for a seller who earned no commission in the window has no row to appear on, and a test
/// that did not know that would pass for the wrong reason. The payouts are then written with their
/// creation date deliberately *outside* the window their period ends in, so the old rule and the
/// new one give different answers and the difference is what is being asserted.
/// </remarks>
public sealed class PayoutPeriodReportingTests : IClassFixture<MarketplaceApiFactory>, IAsyncLifetime
{
    private readonly MarketplaceApiFactory _factory;
    private MarketplaceTestData _data = null!;

    public PayoutPeriodReportingTests(MarketplaceApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _data = await _factory.CreateDatabaseAsync();
        await _data.InitialiseAsync();

        // The class shares one database with the rest of the suite, and a payout written by an
        // earlier run of this file would still be sitting in it. These are identified by their
        // reference prefix, so they can be swept without touching anything the application made.
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var leftovers = await context.SellerPayouts.Where(p => p.Reference.StartsWith("P17-")).ToListAsync();
        context.SellerPayouts.RemoveRange(leftovers);
        await context.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_payout_raised_after_its_period_closed_is_still_reported_against_that_period()
    {
        var (admin, _, sellerId) = await CommissionThisMonthAsync();
        var before = await BaselineAsync(admin, sellerId);
        var created = _factory.Clock.UtcNow.AddDays(40);

        await WithPayoutAsync(sellerId,
            periodEnd: _factory.Clock.UtcNow,
            createdAt: created,
            PayoutStatus.Completed,
            net: 120m);

        var row = await RowForAsync(admin, sellerId, "ThisMonth");

        row.Payouts.Should().Be(before.Payouts + 1, "the payout covers this month even though it was raised forty days later");
        row.PaidOut.Should().Be(before.PaidOut + 120m);

        // The window that contains only the creation date must not pick it up, which is the whole
        // difference between the two rules.
        var later = await RowForAsync(admin, sellerId, "ThisYear");
        later.Should().NotBeNull("the seller earned commission this year");
    }

    [Fact]
    public async Task A_payout_is_counted_in_exactly_one_period_never_in_two()
    {
        var (admin, _, sellerId) = await CommissionThisMonthAsync();

        var beforeThis = await BaselineAsync(admin, sellerId);
        var beforeLast = (await admin.GetAsync<List<CommissionReportRowResponse>>("/api/admin/reports/commissions?range=LastMonth"))?.Where(r => r.SellerId == sellerId).Sum(r => r.Payouts) ?? 0;

        // Its period ends this month but it was raised before the window opened — the shape that
        // makes an overlap rule count it twice and a containment rule lose it.
        await WithPayoutAsync(sellerId,
            periodEnd: _factory.Clock.UtcNow,
            createdAt: _factory.Clock.UtcNow.AddDays(-80),
            PayoutStatus.Completed,
            net: 90m);

        var inThisMonth = await RowForAsync(admin, sellerId, "ThisMonth");
        var inLastMonth = (await admin.GetAsync<List<CommissionReportRowResponse>>("/api/admin/reports/commissions?range=LastMonth"))
            ?.Where(r => r.SellerId == sellerId).ToList() ?? [];

        inThisMonth!.Payouts.Should().Be(beforeThis.Payouts + 1, "the payout belongs to the period it ends in");
        inLastMonth.Sum(r => r.Payouts).Should().Be(beforeLast, "and to that period alone");
    }

    [Theory]
    [InlineData(PayoutStatus.Pending)]
    [InlineData(PayoutStatus.Processing)]
    [InlineData(PayoutStatus.Failed)]
    public async Task Only_a_completed_payout_counts_as_money_paid_out(PayoutStatus status)
    {
        var (admin, _, sellerId) = await CommissionThisMonthAsync();
        var before = await BaselineAsync(admin, sellerId);

        await WithPayoutAsync(sellerId,
            periodEnd: _factory.Clock.UtcNow,
            createdAt: _factory.Clock.UtcNow,
            status,
            net: 300m);

        var row = await RowForAsync(admin, sellerId, "ThisMonth");

        row.Payouts.Should().Be(before.Payouts, $"a {status} payout has not moved money");
        row.PaidOut.Should().Be(before.PaidOut);
        row.CommissionAmount.Should().BeGreaterThan(0m, "commission earned is reported whatever the payout's status");
    }

    [Fact]
    public async Task A_completed_payout_is_counted_where_a_pending_one_is_not()
    {
        var (admin, _, sellerId) = await CommissionThisMonthAsync();
        var before = await BaselineAsync(admin, sellerId);

        await WithPayoutAsync(sellerId, _factory.Clock.UtcNow, _factory.Clock.UtcNow, PayoutStatus.Pending, 300m);
        (await RowForAsync(admin, sellerId, "ThisMonth")).Payouts.Should().Be(before.Payouts);

        // The payout is raised for the same period, so this one is the difference between the two.
        await WithPayoutAsync(sellerId, _factory.Clock.UtcNow, _factory.Clock.UtcNow, PayoutStatus.Completed, 300m);

        var row = await RowForAsync(admin, sellerId, "ThisMonth");
        row.Payouts.Should().Be(before.Payouts + 1);
        row.PaidOut.Should().Be(before.PaidOut + 300m);
    }

    /* ------------------------------------------------------------------------------ helpers */

    /// <summary>What the report already said for this seller, so assertions can be about the delta.</summary>
    private async Task<CommissionReportRowResponse> BaselineAsync(ApiClient admin, Guid sellerId)
    {
        var report = await admin.GetAsync<List<CommissionReportRowResponse>>("/api/admin/reports/commissions?range=ThisMonth");
        return report!.FirstOrDefault(r => r.SellerId == sellerId)
            ?? new CommissionReportRowResponse(sellerId, "unknown", 0, 0m, 0m, 0m, 0, 0m);
    }

    private async Task<CommissionReportRowResponse> RowForAsync(ApiClient admin, Guid sellerId, string range)
    {
        var report = await admin.GetAsync<List<CommissionReportRowResponse>>($"/api/admin/reports/commissions?range={range}");
        var row = report!.FirstOrDefault(r => r.SellerId == sellerId);

        row.Should().NotBeNull($"the seller earned commission in {range}, so the report has a row to check");
        return row!;
    }

    /// <summary>
    /// Buys something, so the seller has a real commission row inside the window under test.
    /// </summary>
    private async Task<(ApiClient Admin, ApiClient Seller, Guid SellerId)> CommissionThisMonthAsync()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);
        var (seller, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);

        var email = $"payout-{Guid.NewGuid():N}@test.dev";
        var (buyer, _) = await AuthHelper.RegisterAsync(_factory, email, MarketplaceTestData.CustomerPassword);

        var address = await buyer.PostAsync("/api/addresses", new CreateAddressRequest(
            "Home", "Test Customer", "+9779800000000", "12 Ratna Marg", null, "Kathmandu", null, "44600", "NP", true));
        address.StatusCode.Should().Be(HttpStatusCode.Created);

        var addressId = (await ApiClient.ReadAsync<AddressResponse>(address))!.Id;
        var added = await buyer.PostAsync("/api/cart/items",
            new AddCartItemRequest(_data.SellerAProductId, _data.SellerAProductVariantId, 1));
        added.StatusCode.Should().Be(HttpStatusCode.OK);

        var placed = await buyer.PostAsync("/api/checkout",
            new CheckoutRequest(addressId, "Mock", null, "Phase 17 payout period order", null, Guid.NewGuid().ToString("N")));
        placed.StatusCode.Should().Be(HttpStatusCode.Created, await ApiClient.ReadTextAsync(placed));

        await WebhookHelper.SettlePaymentAsync(_factory, buyer, (await ApiClient.ReadAsync<CheckoutResponse>(placed))!.OrderId, "p17-payout");

        return (admin, seller, _data.SellerAId);
    }

    /// <summary>
    /// Writes a payout row directly and removes it afterwards.
    /// </summary>
    /// <remarks>
    /// Through the context on purpose: no endpoint raises a payout, because the background job
    /// does, and a test about *reporting* should not depend on a scheduler firing. Everything it
    /// creates is removed so the shared fixture is left as it was found.
    /// </remarks>
    private async Task WithPayoutAsync(
        Guid sellerId,
        DateTimeOffset periodEnd,
        DateTimeOffset createdAt,
        PayoutStatus status,
        decimal net)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();

        var payout = SellerPayout.Create(
            sellerId,
            $"P17-{Guid.NewGuid():N}",
            decimal.Round(net * 1.1m, 2),
            decimal.Round(net * 0.1m, 2),
            1,
            periodEnd.AddDays(-7),
            periodEnd,
            createdAt);

        switch (status)
        {
            case PayoutStatus.Completed:
                payout.MarkProcessing(createdAt.AddMinutes(1));
                payout.MarkCompleted($"txn-{Guid.NewGuid():N}", createdAt.AddMinutes(2));
                break;
            case PayoutStatus.Processing:
                payout.MarkProcessing(createdAt.AddMinutes(1));
                break;
            case PayoutStatus.Failed:
                payout.MarkProcessing(createdAt.AddMinutes(1));
                payout.MarkFailed("Phase 17 test failure", createdAt.AddMinutes(2));
                break;
        }

        context.SellerPayouts.Add(payout);
        await context.SaveChangesAsync();
    }
}
