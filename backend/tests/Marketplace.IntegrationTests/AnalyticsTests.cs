using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Analytics.DTOs;
using Marketplace.Application.Modules.Catalog.DTOs;
using Marketplace.Application.Modules.Orders.DTOs;
using Marketplace.Application.Modules.Payments.DTOs;
using Marketplace.Application.Modules.Sellers.DTOs;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Marketplace.IntegrationTests;

/// <summary>
/// Dashboards are where a scoping mistake turns into a business incident, so every figure is
/// checked against the orders that actually exist. Each scenario measures the change its own
/// order caused rather than an absolute total, because the class shares one seeded seller and
/// one database: an absolute figure would make the suite depend on the order tests happen to
/// run in.
/// </summary>
public sealed class AnalyticsTests : IClassFixture<MarketplaceApiFactory>, IAsyncLifetime
{
    private readonly MarketplaceApiFactory _factory;
    private MarketplaceTestData _data = null!;

    public AnalyticsTests(MarketplaceApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _data = await _factory.CreateDatabaseAsync();
        await _data.InitialiseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_seller_sees_their_own_revenue_and_never_another_sellers()
    {
        var (sellerA, _) = await SignInSellerAsync(MarketplaceTestData.SellerEmail);
        var (sellerB, _) = await SignInSellerAsync(MarketplaceTestData.SecondSellerEmail);

        var beforeA = await sellerA.GetAsync<SellerSummaryResponse>("/api/seller/analytics/summary");
        var beforeB = await sellerB.GetAsync<SellerSummaryResponse>("/api/seller/analytics/summary");

        // Seller A's own product, bought and paid for.
        var (buyerA, _) = await NewCustomerAsync();
        var orderA = await CheckoutAsync(buyerA, _data.SellerAProductId, _data.SellerAProductVariantId, 2);
        await WebhookHelper.SettlePaymentAsync(_factory, buyerA, orderA.OrderId, "scope-a");

        // Seller B's product, in the same marketplace.
        var (buyerB, _) = await NewCustomerAsync();
        var orderB = await CheckoutAsync(buyerB, _data.SellerBProductId, _data.SellerBProductVariantId, 1);
        await WebhookHelper.SettlePaymentAsync(_factory, buyerB, orderB.OrderId, "scope-b");

        var afterA = await sellerA.GetAsync<SellerSummaryResponse>("/api/seller/analytics/summary");
        var afterB = await sellerB.GetAsync<SellerSummaryResponse>("/api/seller/analytics/summary");

        (afterA!.TotalSales - beforeA!.TotalSales).Should().Be(498m, "two units at 249, and only those");
        (afterA.TotalOrders - beforeA.TotalOrders).Should().Be(1);

        (afterB!.TotalSales - beforeB!.TotalSales).Should().Be(89m, "one unit at 89, and only those");
        (afterB.TotalOrders - beforeB.TotalOrders).Should().Be(1);
    }

    [Fact]
    public async Task Revenue_counts_an_order_once_and_never_twice_when_it_is_paid()
    {
        var (seller, _) = await SignInSellerAsync(MarketplaceTestData.SellerEmail);
        var (buyer, _) = await NewCustomerAsync();

        var before = await seller.GetAsync<List<RevenuePointResponse>>("/api/seller/analytics/revenue?range=Last7Days");
        var summary = await seller.GetAsync<SellerSummaryResponse>("/api/seller/analytics/summary");

        var order = await CheckoutAsync(buyer, _data.SellerAProductId, _data.SellerAProductVariantId, 1);

        // The dashboard reports what has been sold, so placing the order is what moves it. What
        // it must never do is count the sale again when the payment settles.
        var placed = await seller.GetAsync<List<RevenuePointResponse>>("/api/seller/analytics/revenue?range=Last7Days");
        (placed!.Sum(p => p.Revenue) - before!.Sum(p => p.Revenue)).Should().Be(249m);

        await WebhookHelper.SettlePaymentAsync(_factory, buyer, order.OrderId, "counted-once");

        var settled = await seller.GetAsync<List<RevenuePointResponse>>("/api/seller/analytics/revenue?range=Last7Days");
        (settled!.Sum(p => p.Revenue) - before!.Sum(p => p.Revenue)).Should().Be(249m, "settling must not sell the same order twice");

        var after = await seller.GetAsync<SellerSummaryResponse>("/api/seller/analytics/summary");
        (after!.TotalSales - summary!.TotalSales).Should().Be(249m);
        (after.PendingEarnings - summary.PendingEarnings).Should().BeGreaterThan(0m, "the seller is owed a cut");
    }

    [Fact]
    public async Task A_revenue_series_ends_on_the_bucket_the_order_landed_in()
    {
        var (seller, _) = await SignInSellerAsync(MarketplaceTestData.SellerEmail);
        var (buyer, _) = await NewCustomerAsync();

        var before = await seller.GetAsync<List<RevenuePointResponse>>("/api/seller/analytics/revenue?range=Last7Days");
        var order = await CheckoutAsync(buyer, _data.SellerAProductId, _data.SellerAProductVariantId, 1);
        await WebhookHelper.SettlePaymentAsync(_factory, buyer, order.OrderId, "series");

        var after = await seller.GetAsync<List<RevenuePointResponse>>("/api/seller/analytics/revenue?range=Last7Days");

        after.Should().NotBeNull();
        after!.Should().HaveCount(7, "a week is seven buckets, whatever else is in the data");
        after.Should().BeInAscendingOrder(p => p.Period, "a time series reads left to right");

        var growth = after!.Sum(p => p.Revenue) - before!.Sum(p => p.Revenue);
        growth.Should().Be(249m, "the series grew by exactly this order");

        after![^1].Revenue.Should().BeGreaterThan(0m, "the most recent bucket is today's, where the order sits");
    }

    [Fact]
    public async Task Top_products_rank_by_units_and_price_the_commission()
    {
        var (seller, _) = await SignInSellerAsync(MarketplaceTestData.SellerEmail);
        var (buyer, _) = await NewCustomerAsync();

        var order = await CheckoutAsync(buyer, _data.SellerAProductId, _data.SellerAProductVariantId, 3);
        await WebhookHelper.SettlePaymentAsync(_factory, buyer, order.OrderId, "top");

        var top = await seller.GetAsync<List<TopProductResponse>>("/api/seller/analytics/top-products?take=5");

        top.Should().NotBeNull();
        top!.Should().NotBeEmpty();

        var entry = top!.Single(t => t.ProductId == _data.SellerAProductId);
        entry.QuantitySold.Should().BeGreaterThanOrEqualTo(3);
        entry.Revenue.Should().BeGreaterThanOrEqualTo(747m);
        entry.Commission.Should().BeGreaterThan(0m, "the seller must see what the platform took");

        top!.Should().BeInDescendingOrder(t => t.QuantitySold, "the best seller comes first");
    }

    [Fact]
    public async Task Category_sales_reconcile_with_the_summary()
    {
        var (seller, _) = await SignInSellerAsync(MarketplaceTestData.SellerEmail);
        var (buyer, _) = await NewCustomerAsync();

        var before = await seller.GetAsync<SellerSummaryResponse>("/api/seller/analytics/summary");
        var order = await CheckoutAsync(buyer, _data.SellerAProductId, _data.SellerAProductVariantId, 2);
        await WebhookHelper.SettlePaymentAsync(_factory, buyer, order.OrderId, "categories");

        var after = await seller.GetAsync<SellerSummaryResponse>("/api/seller/analytics/summary");
        var categories = await seller.GetAsync<List<CategorySalesResponse>>("/api/seller/analytics/sales-by-category");

        categories.Should().NotBeNull();
        categories!.Should().NotBeEmpty();
        categories!.Sum(c => c.Revenue).Should().Be(after!.TotalSales,
            "a figure that appears in two places has to agree in both");
        categories!.Sum(c => c.Share).Should().BeApproximately(100m, 0.01m);
        (after.TotalSales - before!.TotalSales).Should().Be(498m);
    }

    [Fact]
    public async Task A_customer_cannot_read_seller_or_admin_analytics()
    {
        var (customer, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.CustomerEmail, MarketplaceTestData.CustomerPassword);

        (await customer.Http.GetAsync("/api/seller/analytics/summary")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await customer.Http.GetAsync("/api/admin/analytics/summary")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await customer.Http.GetAsync("/api/seller/commissions")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await customer.Http.GetAsync("/api/seller/payouts")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_admin_summary_counts_every_seller_regardless_of_status()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var summary = await admin.GetAsync<AdminSummaryResponse>("/api/admin/analytics/summary");

        summary.Should().NotBeNull();
        summary!.TotalCustomers.Should().BeGreaterThan(0);
        summary.TotalSellers.Should().BeGreaterThanOrEqualTo(4);
        summary.ActiveSellers.Should().BeGreaterThan(0);
        summary.PendingSellers.Should().BeGreaterThan(0, "a pending seller still exists and still counts");
        summary.SuspendedSellers.Should().BeGreaterThan(0, "so does a suspended one");
        summary.TotalProducts.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Admin_revenue_includes_what_a_single_seller_dashboard_shows()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);
        var (sellerB, _) = await SignInSellerAsync(MarketplaceTestData.SecondSellerEmail);
        var (buyer, _) = await NewCustomerAsync();

        var beforeAdmin = await admin.GetAsync<List<RevenuePointResponse>>("/api/admin/analytics/revenue?range=Last7Days");
        var beforeSeller = await sellerB.GetAsync<List<RevenuePointResponse>>("/api/seller/analytics/revenue?range=Last7Days");

        var order = await CheckoutAsync(buyer, _data.SellerBProductId, _data.SellerBProductVariantId, 1);
        await WebhookHelper.SettlePaymentAsync(_factory, buyer, order.OrderId, "admin-revenue");

        var afterAdmin = await admin.GetAsync<List<RevenuePointResponse>>("/api/admin/analytics/revenue?range=Last7Days");
        var afterSeller = await sellerB.GetAsync<List<RevenuePointResponse>>("/api/seller/analytics/revenue?range=Last7Days");

        var adminGrowth = afterAdmin!.Sum(p => p.Revenue) - beforeAdmin!.Sum(p => p.Revenue);
        var sellerGrowth = afterSeller!.Sum(p => p.Revenue) - beforeSeller!.Sum(p => p.Revenue);

        sellerGrowth.Should().Be(89m);
        adminGrowth.Should().BeGreaterThanOrEqualTo(sellerGrowth, "the marketplace view is a superset of one seller's");
    }

    [Fact]
    public async Task Commissions_match_the_rate_the_seller_signed_up_to()
    {
        var (seller, _) = await SignInSellerAsync(MarketplaceTestData.SellerEmail);
        var (buyer, _) = await NewCustomerAsync();

        var order = await CheckoutAsync(buyer, _data.SellerAProductId, _data.SellerAProductVariantId, 2);
        await WebhookHelper.SettlePaymentAsync(_factory, buyer, order.OrderId, "commission");

        var commissions = await seller.GetAsync<PagedResult<CommissionResponse>>("/api/seller/commissions?pageSize=100");

        commissions.Should().NotBeNull();
        var sellerOrderId = await SellerOrderIdFor(order.OrderId);
        var commission = commissions!.Items.Single(c => c.SellerOrderId == sellerOrderId);
        commission.SellerId.Should().Be(_data.SellerAId);

        // Seller A is seeded with a ten percent rate.
        // Rates are held as a percentage, the way a seller signed up to them.
        commission.Rate.Should().Be(10m);
        commission.CommissionAmount.Should().Be(decimal.Round(commission.GrossAmount * commission.Rate / 100m, 2));
        commission.SellerAmount.Should().Be(decimal.Round(commission.GrossAmount - commission.CommissionAmount, 2));
    }

    [Fact]
    public async Task Suspending_a_seller_takes_their_catalogue_offline_immediately()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);
        var (seller, _) = await SignInSellerAsync(MarketplaceTestData.SellerEmail);

        var before = await seller.Http.GetAsync("/api/seller/products");
        before.StatusCode.Should().Be(HttpStatusCode.OK, "the seller starts out able to work");

        var suspend = await admin.PutAsync(
            $"/api/sellers/{_data.SellerAId}/status",
            new UpdateSellerStatusRequest(SellerStatus.Suspended, "under review", null));

        suspend.StatusCode.Should().Be(HttpStatusCode.OK, await ApiClient.ReadTextAsync(suspend));

        // A valid, fully formed request: the refusal has to come from the seller's standing,
        // not from a malformed body being rejected first.
        var after = await seller.PostAsync("/api/seller/products", CreateProductRequest(_data.CategoryId));

        after.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a suspended seller must lose the ability to change their catalogue");
    }

    private CreateProductRequest CreateProductRequest(Guid categoryId) => new(
        "Suspended Seller Attempt",
        null,
        "short",
        "long",
        categoryId,
        10m,
        null,
        "Brand",
        "Model",
        [new CreateProductImageRequest("https://cdn.test/x.jpg", "x", true)],
        [new CreateProductVariantRequest($"SKU-{Guid.NewGuid():N}"[..20], "Default", null, 5, 2, [])],
        [],
        []);

    /// <summary>
    /// The sub-order a commission is booked against. A seller reconciles against the sub-order,
    /// so the scenario needs its id rather than the marketplace order's.
    /// </summary>
    private async Task<Guid> SellerOrderIdFor(Guid orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();

        return await context.SellerOrders.AsNoTracking()
            .Where(so => so.OrderId == orderId)
            .Select(so => so.Id)
            .FirstAsync();
    }

    private Task<(ApiClient Client, SessionTokens Tokens)> SignInSellerAsync(string email) =>
        AuthHelper.SignInAsync(_factory, email, MarketplaceTestData.SellerPassword);

    private async Task<(ApiClient Client, Guid AddressId)> NewCustomerAsync()
    {
        var email = $"analytics-{Guid.NewGuid():N}@test.dev";
        var (client, _) = await AuthHelper.RegisterAsync(_factory, email, MarketplaceTestData.CustomerPassword);

        var address = await client.PostAsync("/api/addresses", new CreateAddressRequest(
            "Home", "Test Customer", "+9779800000000", "12 Ratna Marg", null, "Kathmandu", null, "44600", "NP", true));

        address.StatusCode.Should().Be(HttpStatusCode.Created);
        return (client, (await ApiClient.ReadAsync<AddressResponse>(address))!.Id);
    }

    private async Task<CheckoutResponse> CheckoutAsync(ApiClient customer, Guid productId, Guid variantId, int quantity)
    {
        await customer.PostAsync("/api/cart/items", new AddCartItemRequest(productId, variantId, quantity));

        var addresses = await customer.GetAsync<List<AddressResponse>>("/api/addresses");
        var response = await customer.PostAsync("/api/checkout",
            new CheckoutRequest(addresses![0].Id, "Mock", null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await ApiClient.ReadTextAsync(response));
        return (await ApiClient.ReadAsync<CheckoutResponse>(response))!;
    }
}