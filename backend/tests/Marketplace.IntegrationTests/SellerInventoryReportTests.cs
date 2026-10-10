using System.Net;
using FluentAssertions;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Analytics.DTOs;
using Marketplace.Domain.Enums;
using Marketplace.IntegrationTests.Infrastructure;
using Xunit;

namespace Marketplace.IntegrationTests;

/// <summary>
/// The seller-facing stock report, and the boundaries around it.
/// </summary>
/// <remarks>
/// The point of this file is that a seller can only ever read their own stock. That is enforced by
/// the seller id coming from the caller's principal and nowhere else, so the route has no seller
/// parameter to tamper with — and these tests prove it by signing in as one seller and looking for
/// the other's products rather than by inspecting the query.
/// </remarks>
public sealed class SellerInventoryReportTests : IClassFixture<MarketplaceApiFactory>, IAsyncLifetime
{
    private readonly MarketplaceApiFactory _factory;
    private MarketplaceTestData _data = null!;

    public SellerInventoryReportTests(MarketplaceApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _data = await _factory.CreateDatabaseAsync();
        await _data.InitialiseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private const string Route = "/api/seller/reports/inventory";

    [Fact]
    public async Task A_seller_reads_their_own_stock_and_only_their_own()
    {
        var (seller, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);

        var report = await seller.GetAsync<PagedResult<InventoryReportRowResponse>>($"{Route}?page=1&pageSize=100");

        report.Should().NotBeNull();
        report!.TotalCount.Should().BeGreaterThan(0, "the seeded seller lists products, so the report is not empty");

        // Every row belongs to this seller's store, and no row belongs to the other one.
        report.Items.Should().OnlyContain(row => row.StoreName == "Tech World");
        report.Items.Should().NotContain(row => row.StoreName == "Fashion Hub");
    }

    [Fact]
    public async Task A_seller_cannot_reach_another_sellers_stock_through_any_parameter()
    {
        var (seller, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);

        // The route takes no seller parameter at all, so these are ignored rather than obeyed: a
        // seller who guesses the other seller's id, and one who supplies their own, both get their
        // own stock.
        foreach (var attempt in new[]
        {
            $"{Route}?page=1&sellerId={_data.SellerBId}",
            $"{Route}?page=1&sellerId={_data.SellerAId}",
            $"{Route}?page=1&storeId={_data.SellerBId}",
        })
        {
            var report = await seller.GetAsync<PagedResult<InventoryReportRowResponse>>(attempt);
            report!.Items.Should().OnlyContain(row => row.StoreName == "Tech World", $"'{attempt}' must not widen the report");
        }

        // Searching for the other seller's products finds nothing, because they are not in scope.
        // The other seller's catalogue is denim; this seller lists none, so the honest answer is
        // no rows rather than rows belonging to somebody else.
        var byName = await seller.GetAsync<PagedResult<InventoryReportRowResponse>>($"{Route}?page=1&search=denim");
        byName!.Items.Should().BeEmpty("another seller's products are not in this report to be found");
    }

    [Fact]
    public async Task The_report_is_refused_to_anyone_who_is_not_a_seller()
    {
        var (customer, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.CustomerEmail, MarketplaceTestData.CustomerPassword);
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        (await _factory.CreateClient().GetAsync(Route)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "an anonymous caller is not told whether the route exists");
        (await customer.Http.GetAsync(Route)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await admin.Http.GetAsync(Route)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden,
                "the platform-wide view that can select a seller is the admin report, not this route");
    }

    [Fact]
    public async Task The_report_pages_and_reports_a_total()
    {
        var (seller, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);

        var first = await seller.GetAsync<PagedResult<InventoryReportRowResponse>>($"{Route}?page=1&pageSize=1");
        var everything = await seller.GetAsync<PagedResult<InventoryReportRowResponse>>($"{Route}?page=1&pageSize=100");

        first!.Items.Should().ContainSingle();
        first.PageSize.Should().Be(1);
        first.TotalCount.Should().Be(everything!.TotalCount, "the total does not depend on the page size");
        first.TotalPages.Should().Be((int)Math.Ceiling(first.TotalCount / (double)first.PageSize));

        if (first.TotalPages > 1)
        {
            var second = await seller.GetAsync<PagedResult<InventoryReportRowResponse>>($"{Route}?page=2&pageSize=1");
            second!.Items.Single().ProductId.Should().NotBe(first.Items.Single().ProductId);
        }

        var capped = await seller.GetAsync<PagedResult<InventoryReportRowResponse>>($"{Route}?page=1&pageSize=5000");
        capped!.PageSize.Should().Be(PageRequest.MaxPageSize);
    }

    [Fact]
    public async Task The_report_is_ordered_lowest_stock_first_and_searches_without_regard_to_case()
    {
        var (seller, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);

        var everything = await seller.GetAsync<PagedResult<InventoryReportRowResponse>>($"{Route}?page=1&pageSize=100");
        everything!.Items.Should().BeInAscendingOrder(row => row.Available);

        var sample = everything.Items.First(i => i.ProductName.Length > 2);
        foreach (var term in new[] { sample.ProductName, sample.ProductName.ToLowerInvariant(), sample.ProductName.ToUpperInvariant() })
        {
            var found = await seller.GetAsync<PagedResult<InventoryReportRowResponse>>(
                $"{Route}?page=1&pageSize=50&search={Uri.EscapeDataString(term)}");

            found!.TotalCount.Should().BeGreaterThan(0, "the product is searchable in any case");
            found.Items.Should().OnlyContain(row => row.StoreName == "Tech World");
        }
    }

    [Fact]
    public async Task The_stock_filters_mean_what_they_mean_on_the_admin_report()
    {
        var (seller, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);

        var low = await seller.GetAsync<PagedResult<InventoryReportRowResponse>>($"{Route}?page=1&pageSize=100&lowStockOnly=true");
        low!.Items.Should().OnlyContain(row => row.Available - row.Reserved <= row.Threshold,
            "low stock is sellable quantity against the variant's own threshold");

        var outOfStock = await seller.GetAsync<PagedResult<InventoryReportRowResponse>>($"{Route}?page=1&pageSize=100&outOfStockOnly=true");

        // A loop rather than OnlyContain because this collection is legitimately empty when the
        // seller has nothing out of stock, and an assertion over no rows should say so plainly
        // rather than fail.
        foreach (var row in outOfStock!.Items)
        {
            row.Available.Should().BeLessThanOrEqualTo(0, "out of stock means no available quantity");
            row.StoreName.Should().Be("Tech World");
        }
    }
}