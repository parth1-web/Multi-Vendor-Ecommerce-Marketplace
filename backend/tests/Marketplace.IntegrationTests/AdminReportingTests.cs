using System.Net;
using System.Text.Json;
using FluentAssertions;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Analytics.Abstractions;
using Marketplace.Application.Modules.Analytics.DTOs;
using Marketplace.Application.Modules.Catalog.DTOs;
using Marketplace.Application.Modules.Auth.DTOs;
using Marketplace.Application.Modules.Notifications.DTOs;
using Marketplace.Application.Modules.Coupons.DTOs;
using Marketplace.Application.Modules.Reviews.DTOs;
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
/// The admin reporting and audit endpoints after Phase 16: search that ignores capitalisation,
/// reports whose money columns come from the orders that recorded them, reports that page in the
/// database, and an export that is streamed, bounded and recorded.
/// </summary>
/// <remarks>
/// The financial tests deliberately place real orders — one with a coupon, one with a completed
/// refund — because a report asserted against a seeded dataset of zeroes proves nothing about
/// whether the zeroes come from arithmetic or from a constant.
/// </remarks>
public sealed class AdminReportingTests : IClassFixture<MarketplaceApiFactory>, IAsyncLifetime
{
    private readonly MarketplaceApiFactory _factory;
    private MarketplaceTestData _data = null!;

    public AdminReportingTests(MarketplaceApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _data = await _factory.CreateDatabaseAsync();
        await _data.InitialiseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly string[] ReportRoutes =
    [
        "/api/admin/reports/sales",
        "/api/admin/reports/sellers",
        "/api/admin/reports/inventory",
        "/api/admin/reports/commissions",
        "/api/admin/audit-logs",
        "/api/admin/reports/export/sales.csv",
    ];

    /* =========================================================== search ignores capitalisation */

    [Theory]
    [InlineData("/api/admin/audit-logs?page=1&pageSize=50&search={term}", false)]
    [InlineData("/api/admin/users?page=1&pageSize=50&search={term}", false)]
    [InlineData("/api/products?page=1&pageSize=50&search={term}", false)]
    [InlineData("/api/admin/products?page=1&pageSize=50&search={term}", false)]
    [InlineData("/api/admin/orders?page=1&pageSize=50&search={term}", false)]
    [InlineData("/api/coupons?page=1&pageSize=50&search={term}", false)]
    [InlineData("/api/inventory?page=1&pageSize=50&search={term}", false)]
    [InlineData("/api/admin/reviews?page=1&pageSize=50&search={term}", false)]
    [InlineData("/api/payments?page=1&pageSize=50&search={term}", false)]
    [InlineData("/api/refunds/all?page=1&pageSize=50&search={term}", false)]
    [InlineData("/api/seller/products?page=1&pageSize=50&search={term}", true)]
    [InlineData("/api/seller/orders?page=1&pageSize=50&search={term}", true)]
    public async Task Searching_the_same_thing_differently_capped_finds_the_same_rows(string route, bool sellerScoped)
    {
        // A seller-only route is asked as a seller. Asking it as an admin would prove that the
        // route is locked down, which another test already covers, not that its search works.
        var (client, _) = sellerScoped
            ? await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword)
            : await AuthHelper.SignInAsync(_factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        // The seeded seller's email and the seeded admin's are both lower case in storage, so the
        // mixed-case variant is the one that proves the comparison is not a coincidence of casing.
        var term = MarketplaceTestData.SellerEmail.Split('@')[0];
        var exact = await CountAsync(client, route.Replace("{term}", term));
        var lower = await CountAsync(client, route.Replace("{term}", term.ToLowerInvariant()));
        var upper = await CountAsync(client, route.Replace("{term}", term.ToUpperInvariant()));
        var mixed = await CountAsync(client, route.Replace("{term}", FlipCase(term)));

        if (exact > 0)
        {
            lower.Should().Be(exact, $"{route} must not depend on the case of the term");
            upper.Should().Be(exact, $"{route} must not depend on the case of the term");
            mixed.Should().Be(exact, $"{route} must not depend on the case of the term");
        }

        // And a term that is genuinely absent must stay absent in every casing, or "insensitive"
        // would simply mean "matches more".
        var absent = await CountAsync(client, route.Replace("{term}", "zzzz-no-such-record-zzzz"));
        absent.Should().Be(0, $"{route} must not invent matches");
    }

    [Fact]
    public async Task A_product_search_finds_the_seeded_listing_in_any_case()
    {
        var product = "";

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            product = await context.Products.AsNoTracking().Where(p => p.Id == _data.SellerAProductId).Select(p => p.Name).FirstAsync();
        }

        var lower = await PublicProductSearchAsync(product.ToLowerInvariant());
        var exact = await PublicProductSearchAsync(product);
        var upper = await PublicProductSearchAsync(product.ToUpperInvariant());

        exact.Should().BeGreaterThan(0, "the seeded product is searchable by its own name");
        lower.Should().Be(exact);
        upper.Should().Be(exact);
    }

    [Fact]
    public async Task A_coupon_code_search_ignores_case_and_still_filters_and_pages()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var upper = await admin.GetAsync<PagedResult<CouponResponse>>("/api/coupons?page=1&pageSize=10&search=TEST10");
        var lower = await admin.GetAsync<PagedResult<CouponResponse>>("/api/coupons?page=1&pageSize=10&search=test10");

        upper!.TotalCount.Should().Be(1);
        lower!.TotalCount.Should().Be(1);
        lower.Items.Single().Code.Should().Be("TEST10", "the stored code keeps its own casing whatever was typed");

        var none = await admin.GetAsync<PagedResult<CouponResponse>>("/api/coupons?page=1&pageSize=10&search=WELCOME11");
        none!.TotalCount.Should().Be(0);
    }

    /* ================================================================ sales report: real money */

    [Fact]
    public async Task The_sales_report_reports_the_discount_a_coupon_actually_gave()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var (withCouponClient, withCoupon) = await CheckoutAsync("TEST10");
        var period = await ReportFor(withCoupon.OrderNumber);

        period.Should().NotBeNull();
        period!.Discounts.Should().BeGreaterThan(0m, "the order carried a coupon, so the report cannot say zero");
        period.GrossRevenue.Should().BeGreaterThan(period.Discounts);
        period.NetRevenue.Should().Be(decimal.Round(period.GrossRevenue - period.Discounts - period.Refunds, 2),
            "net revenue is gross less discounts less refunds, and nothing else");
        period.NetRevenue.Should().NotBe(period.GrossRevenue, "which is the whole point of having both columns");
    }

    [Fact]
    public async Task The_sales_report_reports_tax_and_shipping_the_checkout_charged()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var (orderClient, order) = await CheckoutAsync(coupon: null);
        var period = await ReportFor(order.OrderNumber);

        period.Should().NotBeNull();
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var stored = await context.Orders.AsNoTracking()
            .Where(o => o.Id == order.OrderId)
            .Select(o => new { o.TaxAmount, o.ShippingAmount })
            .FirstAsync();

        period!.Tax.Should().Be(decimal.Round(stored.TaxAmount, 2), "the report reads the tax the order recorded");
        period.Shipping.Should().Be(decimal.Round(stored.ShippingAmount, 2), "and the shipping it recorded");
        (period.NetRevenue == stored.TaxAmount).Should().BeFalse(
            "net revenue is goods retained; tax is reported in its own column rather than decided upon here");
    }

    [Fact]
    public async Task The_sales_report_counts_only_money_that_was_actually_returned()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var (orderClient, order) = await CheckoutAsync(coupon: null);
        await DeliverAsync(new PlacedOrder(orderClient, order.OrderId, order.OrderNumber));
        var itemId = await FirstOrderItemIdAsync(order.OrderId);

        // A request nobody has acted on is not a refund.
        var pending = await orderClient.PostAsync("/api/refunds", new CreateRefundRequest(order.OrderId, [itemId], "damaged", null));
        pending.StatusCode.Should().Be(HttpStatusCode.Created);
        var refund = (await ApiClient.ReadAsync<RefundResponse>(pending))!;

        var whilePending = await ReportFor(order.OrderNumber);
        whilePending!.Refunds.Should().Be(0m, "an unapproved request has not returned any money");

        var approved = await admin.PutAsync($"/api/refunds/{refund.Id}/status",
            new ReviewRefundRequest(ReviewRefundAction.Approve, "Phase 16 refund for the report test"));
        approved.StatusCode.Should().Be(HttpStatusCode.OK, await ApiClient.ReadTextAsync(approved));

        var afterApproval = await ReportFor(order.OrderNumber);
        afterApproval!.Refunds.Should().BeGreaterThan(0m, "an approved refund has returned money and must be reported");
        afterApproval.NetRevenue.Should().Be(
            decimal.Round(afterApproval.GrossRevenue - afterApproval.Discounts - afterApproval.Refunds, 2));
    }

    [Fact]
    public async Task The_sales_report_leaves_a_cancelled_order_out_of_revenue()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        // The bucket is a whole month shared with every other order in this class, so the claim is
        // about what this order contributed, not about an absolute count.
        var (cancelledClient, cancelled) = await CheckoutAsync(coupon: null);
        var before = await ReportFor(cancelled.OrderNumber);
        before.Should().NotBeNull();

        var cancel = await cancelledClient.PostAsync($"/api/orders/{cancelled.OrderId}/cancel", new { reason = "Phase 16 test" });
        cancel.StatusCode.Should().Be(HttpStatusCode.NoContent, await ApiClient.ReadTextAsync(cancel));

        var after = await ReportFor(cancelled.OrderNumber);
        after.Should().NotBeNull();
        after!.Orders.Should().Be(before!.Orders - 1, "a cancelled order stops counting, so the bucket loses exactly it");
    }

    /* ================================================================ seller report: filtered */

    [Fact]
    public async Task The_seller_report_pages_rather_than_returning_everything()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var first = await admin.GetAsync<PagedResult<SellerReportRowResponse>>("/api/admin/reports/sellers?page=1&pageSize=1");

        first.Should().NotBeNull();
        first!.Items.Should().ContainSingle();
        first.Page.Should().Be(1);
        first.PageSize.Should().Be(1);
        first.TotalCount.Should().BeGreaterThanOrEqualTo(2, "the fixture has more than one seller to page through");
        first.TotalPages.Should().Be(first.TotalCount);

        var second = await admin.GetAsync<PagedResult<SellerReportRowResponse>>("/api/admin/reports/sellers?page=2&pageSize=1");
        second!.Items.Should().ContainSingle();
        second.Items[0].SellerId.Should().NotBe(first.Items[0].SellerId, "page two is a different seller");

        var capped = await admin.GetAsync<PagedResult<SellerReportRowResponse>>("/api/admin/reports/sellers?page=1&pageSize=5000");
        capped!.PageSize.Should().Be(PageRequest.MaxPageSize, "the page size is bounded, not honoured");
    }

    [Fact]
    public async Task The_seller_report_filters_by_name_in_any_case_and_by_status()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var all = await admin.GetAsync<PagedResult<SellerReportRowResponse>>("/api/admin/reports/sellers?page=1&pageSize=100");
        var target = all!.Items.First(r => r.StoreName.Length > 3);

        foreach (var term in new[] { target.StoreName, target.StoreName.ToLowerInvariant(), target.StoreName.ToUpperInvariant() })
        {
            var found = await admin.GetAsync<PagedResult<SellerReportRowResponse>>(
                $"/api/admin/reports/sellers?page=1&pageSize=100&search={Uri.EscapeDataString(term)}");

            found!.TotalCount.Should().BeGreaterThan(0);
            found.Items.Should().OnlyContain(row => row.StoreName.Contains(target.StoreName, StringComparison.OrdinalIgnoreCase));
        }

        var byBusinessName = await admin.GetAsync<PagedResult<SellerReportRowResponse>>(
            $"/api/admin/reports/sellers?page=1&pageSize=100&search={Uri.EscapeDataString(target.StoreName.Replace("Hub", "").Replace(" ", ""))}");
        byBusinessName!.TotalCount.Should().BeGreaterThanOrEqualTo(0);

        var active = await admin.GetAsync<PagedResult<SellerReportRowResponse>>("/api/admin/reports/sellers?page=1&pageSize=100&status=Active");
        active!.Items.Should().OnlyContain(row => row.Status == SellerStatus.Active);

        var suspended = await admin.GetAsync<PagedResult<SellerReportRowResponse>>("/api/admin/reports/sellers?page=1&pageSize=100&status=Suspended");
        suspended!.Items.Should().OnlyContain(row => row.Status == SellerStatus.Suspended);

        var none = await admin.GetAsync<PagedResult<SellerReportRowResponse>>("/api/admin/reports/sellers?page=1&pageSize=100&search=zzzz-no-such-store-zzzz");
        none!.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task The_seller_report_measures_a_seller_only_within_the_period_it_is_given()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var lifetime = await admin.GetAsync<PagedResult<SellerReportRowResponse>>("/api/admin/reports/sellers?page=1&pageSize=100");
        var thisMonth = await admin.GetAsync<PagedResult<SellerReportRowResponse>>(
            "/api/admin/reports/sellers?page=1&pageSize=100&range=ThisMonth");
        var lastYear = await admin.GetAsync<PagedResult<SellerReportRowResponse>>(
            "/api/admin/reports/sellers?page=1&pageSize=100&range=LastMonth");

        var justOrdered = await CheckoutPlacedAsync();

        var lifetimeAfter = await admin.GetAsync<PagedResult<SellerReportRowResponse>>("/api/admin/reports/sellers?page=1&pageSize=100");
        var monthAfter = await admin.GetAsync<PagedResult<SellerReportRowResponse>>(
            "/api/admin/reports/sellers?page=1&pageSize=100&range=ThisMonth");
        var oldWindow = await admin.GetAsync<PagedResult<SellerReportRowResponse>>(
            "/api/admin/reports/sellers?page=1&pageSize=100&range=LastMonth");

        var sellerId = SellerIdFor(justOrdered);
        var lifetimeRow = lifetimeAfter!.Items.Single(r => r.SellerId == sellerId);
        var monthRow = monthAfter!.Items.Single(r => r.SellerId == sellerId);

        monthRow.Orders.Should().BeGreaterThanOrEqualTo(lifetimeRow.Orders,
            "a month cannot contain more orders than all time");
        oldWindow!.Items.Should().NotContain(r => r.SellerId == sellerId && r.Orders > 0,
            "the order was placed now, so a window that ended before now cannot contain it");
        lifetime!.TotalCount.Should().BeGreaterThan(0);
        thisMonth!.TotalCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task The_seller_report_rates_a_seller_from_the_reviews_a_shopper_can_see()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var before = await admin.GetAsync<PagedResult<SellerReportRowResponse>>("/api/admin/reports/sellers?page=1&pageSize=100");

        // A review from a customer who bought and received the product, rated 1.
        var delivered = await DeliveredOrderAsync();
        var created = await delivered.Client.PostAsync($"/api/products/{delivered.ProductId}/reviews",
            new CreateReviewRequest(1, "Phase 16 rating check", "One star, honestly given.", await FirstOrderItemIdAsync(delivered.OrderId)));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await ApiClient.ReadTextAsync(created));

        var after = await admin.GetAsync<PagedResult<SellerReportRowResponse>>("/api/admin/reports/sellers?page=1&pageSize=100");
        var rated = after!.Items.Single(r => r.SellerId == SellerIdFor(delivered));

        rated.AverageRating.Should().NotBeNull("a seller with a visible review has a rating");
        rated.AverageRating.Should().Be(1m, "and it is the rating that was actually given");
        rated.ReviewCount.Should().BeGreaterThan(0);
        before!.Items.Should().NotContain(r => r.SellerId == rated.SellerId && r.ReviewCount > 0,
            "there was no review before this test wrote one");

        // Hidden reviews stop counting, because they are not ones anybody can see.
        var review = (await ApiClient.ReadAsync<ReviewResponse>(created))!;
        await admin.PutAsync($"/api/reviews/{review.Id}/visibility", new { isVisible = false, note = (string?)null });

        var hidden = await admin.GetAsync<PagedResult<SellerReportRowResponse>>("/api/admin/reports/sellers?page=1&pageSize=100");
        hidden!.Items.Single(r => r.SellerId == rated.SellerId).ReviewCount.Should().Be(0);
    }

    [Fact]
    public async Task A_seller_with_no_visible_reviews_has_no_rating_rather_than_a_zero_one()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var report = await admin.GetAsync<PagedResult<SellerReportRowResponse>>("/api/admin/reports/sellers?page=1&pageSize=100");

        report!.Items.Should().NotBeEmpty();
        report.Items.Should().OnlyContain(row => row.AverageRating == null || row.AverageRating > 0m,
            "zero is a score nobody gave; no reviews is no score at all");
    }

    /* ============================================================= inventory report: filtered */

    [Fact]
    public async Task The_inventory_report_pages_and_reports_a_total_rather_than_a_ceiling()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var page = await admin.GetAsync<PagedResult<InventoryReportRowResponse>>("/api/admin/reports/inventory?page=1&pageSize=5");

        page.Should().NotBeNull();
        page!.Items.Should().HaveCountLessThanOrEqualTo(5);
        page.TotalCount.Should().BeGreaterThanOrEqualTo(page.Items.Count);
        page.TotalPages.Should().Be((int)Math.Ceiling(page.TotalCount / (double)page.PageSize));

        var all = await admin.GetAsync<PagedResult<InventoryReportRowResponse>>("/api/admin/reports/inventory?page=1&pageSize=100");
        all!.TotalCount.Should().Be(page.TotalCount, "the total does not depend on the page size");

        var second = await admin.GetAsync<PagedResult<InventoryReportRowResponse>>("/api/admin/reports/inventory?page=2&pageSize=5");
        if (page.TotalPages > 1)
        {
            second!.Items.Select(i => i.Sku).Should().NotIntersectWith(page.Items.Select(i => i.Sku));
        }
    }

    [Fact]
    public async Task The_inventory_report_orders_by_available_quantity_lowest_first()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var page = await admin.GetAsync<PagedResult<InventoryReportRowResponse>>("/api/admin/reports/inventory?page=1&pageSize=100");

        page!.Items.Should().OnlyContain(row => row.Available >= 0);
        page.Items.Should().BeInAscendingOrder(row => row.Available);
    }

    [Fact]
    public async Task The_inventory_report_low_stock_filter_uses_the_variants_own_threshold()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        // Driven relative to one variant's own numbers, because "low" is a relationship between
        // what is sellable and what that variant says is low. No fixed quantity is involved.
        var (seller, variantId, sku) = await FirstSellerVariantAsync();
        var row = await ReportRowForAsync(admin, sku);
        var original = row.Threshold;

        try
        {
            // The threshold that puts this variant exactly on the line is its sellable quantity:
            // anything lower says low, anything above does not. Using the report's own reserved
            // figure keeps the arithmetic honest even if another test is holding stock.
            var sellable = row.Available - row.Reserved;

            await SetThresholdAsync(seller, variantId, sellable);

            var atThreshold = await admin.GetAsync<PagedResult<InventoryReportRowResponse>>("/api/admin/reports/inventory?page=1&pageSize=100&lowStockOnly=true");
            atThreshold!.Items.Should().Contain(r => r.Sku == sku, "sellable stock exactly at the threshold counts as low");
            atThreshold.Items.Should().OnlyContain(r => r.Available - r.Reserved <= r.Threshold,
                "every row the filter returned really is at or below its own threshold");

            // One below the threshold is not.
            await SetThresholdAsync(seller, variantId, sellable - 1);

            var aboveThreshold = await admin.GetAsync<PagedResult<InventoryReportRowResponse>>("/api/admin/reports/inventory?page=1&pageSize=100&lowStockOnly=true");
            aboveThreshold!.Items.Should().NotContain(r => r.Sku == sku, "one above its own threshold is not low stock");
        }
        finally
        {
            await SetThresholdAsync(seller, variantId, original);
        }
    }

    [Fact]
    public async Task The_inventory_report_out_of_stock_filter_means_no_available_quantity()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);
        var (seller, variantId, sku) = await FirstSellerVariantAsync();
        var row = await ReportRowForAsync(admin, sku);

        try
        {
            await seller.PutAsync($"/api/inventory/{variantId}", new { delta = -row.Available, reason = "Phase 16 out of stock" });

            var outOfStock = await admin.GetAsync<PagedResult<InventoryReportRowResponse>>("/api/admin/reports/inventory?page=1&pageSize=100&outOfStockOnly=true");
            outOfStock!.Items.Should().OnlyContain(r => r.Available <= 0);
            outOfStock.Items.Should().Contain(r => r.Sku == sku);
        }
        finally
        {
            // The fixture is shared across the class. Draining a seller's stock and leaving it
            // drained makes every later checkout fail with "out of stock", which is a failure about
            // the tests rather than about the code.
            await seller.PutAsync($"/api/inventory/{variantId}", new { delta = row.Available, reason = "Phase 16 restored" });
        }
    }

    [Fact]
    public async Task The_inventory_report_searches_product_sku_and_store_in_any_case()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var all = await admin.GetAsync<PagedResult<InventoryReportRowResponse>>("/api/admin/reports/inventory?page=1&pageSize=100");
        var row = all!.Items.First(i => i.ProductName != "Unknown product");

        foreach (var term in new[] { row.ProductName, row.ProductName.ToLowerInvariant(), row.ProductName.ToUpperInvariant() })
        {
            var found = await admin.GetAsync<PagedResult<InventoryReportRowResponse>>(
                $"/api/admin/reports/inventory?page=1&pageSize=100&search={Uri.EscapeDataString(term)}");

            found!.TotalCount.Should().BeGreaterThan(0, "the product name is searchable in any case");
            found.Items.Should().OnlyContain(r => r.ProductName.Contains(row.ProductName, StringComparison.OrdinalIgnoreCase));
        }

        var byStore = await admin.GetAsync<PagedResult<InventoryReportRowResponse>>(
            $"/api/admin/reports/inventory?page=1&pageSize=100&search={Uri.EscapeDataString(row.StoreName.ToUpperInvariant())}");
        byStore!.TotalCount.Should().BeGreaterThan(0, "the store name is searchable too");

        var bySku = await admin.GetAsync<PagedResult<InventoryReportRowResponse>>(
            $"/api/admin/reports/inventory?page=1&pageSize=100&search={Uri.EscapeDataString(row.Sku.ToLowerInvariant())}");
        bySku!.TotalCount.Should().BeGreaterThan(0, "and so is the SKU");

        var none = await admin.GetAsync<PagedResult<InventoryReportRowResponse>>(
            "/api/admin/reports/inventory?page=1&pageSize=100&search=zzzz-no-such-variant-zzzz");
        none!.TotalCount.Should().Be(0);
    }

    /* =========================================================== commission report: period */

    [Fact]
    public async Task The_commission_report_scopes_its_payout_columns_to_the_window()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var (orderClient, order) = await CheckoutAsync(coupon: null);
        await DeliverAsync(new PlacedOrder(orderClient, order.OrderId, order.OrderNumber));

        var now = await admin.GetAsync<List<CommissionReportRowResponse>>("/api/admin/reports/commissions?range=ThisMonth");
        var older = await admin.GetAsync<List<CommissionReportRowResponse>>("/api/admin/reports/commissions?range=LastMonth");

        var row = now!.FirstOrDefault(r => r.Orders > 0);
        row.Should().NotBeNull("a paid order earns commission inside the window");
        row!.SellerEarnings.Should().BeGreaterThan(0m);
        older.Should().NotContain(r => r.SellerId == row.SellerId && r.Orders > 0,
            "an order placed now cannot appear in a window that has already closed");
    }

    /* ==================================================================== CSV export behaviour */

    [Fact]
    public async Task The_sales_csv_is_a_bounded_file_about_the_period_that_was_asked_for()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var recent = await CheckoutPlacedAsync();

        var response = await admin.Http.GetAsync("/api/admin/reports/export/sales.csv?range=Last7Days");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition!.FileName.Should().Contain("sales-");

        response.Headers.GetValues("X-Export-Row-Limit").Single()
            .Should().Be(IReportService.SalesExportRowLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var text = await ApiClient.ReadTextAsync(response);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].TrimEnd().Should().Be("Order Number,Date,Status,Subtotal,Discount,Shipping,Tax,Total,Refunded,Items,Sellers");
        text.Should().Contain(recent.OrderNumber);

        // The window is real: an order from before it is not in the file.
        var narrow = await ApiClient.ReadTextAsync(await admin.Http.GetAsync("/api/admin/reports/export/sales.csv?range=Last7Days"));
        var wide = await ApiClient.ReadTextAsync(await admin.Http.GetAsync("/api/admin/reports/export/sales.csv?range=ThisYear"));
        wide.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length
            .Should().BeGreaterThanOrEqualTo(narrow.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task An_export_is_recorded_in_the_audit_log_with_what_it_covered()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var before = await admin.GetAsync<PagedResult<AuditLogResponse>>("/api/admin/audit-logs?page=1&pageSize=1&action=ReportExported");
        await CheckoutAsync(coupon: null);
        (await admin.Http.GetAsync("/api/admin/reports/export/sales.csv?range=Last7Days")).EnsureSuccessStatusCode();

        var page = await admin.GetAsync<PagedResult<AuditLogResponse>>("/api/admin/audit-logs?page=1&pageSize=50&action=ReportExported");

        page!.Items.Should().NotBeEmpty("taking the data out is exactly the kind of act that is recorded");
        page.TotalCount.Should().BeGreaterThan(before!.TotalCount);

        var entry = page.Items.First();
        entry.ActorEmail.Should().Be(MarketplaceTestData.AdminEmail);
        entry.EntityType.Should().Be("SalesReport");
        entry.ChangesJson.Should().NotBeNull();
        entry.ChangesJson.Should().Contain("rowCount");
        entry.ChangesJson.Should().Contain("Last7Days");
        entry.ChangesJson.Should().Contain("truncated");
        entry.ChangesJson.Should().NotContain("Bearer");
        entry.ChangesJson.Should().NotContain(MarketplaceTestData.AdminPassword);
    }

    [Fact]
    public async Task An_export_carries_no_customer_pii_beyond_what_a_refund_ledger_already_does()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var (orderClient, order) = await CheckoutAsync(coupon: null);
        var csv = await ApiClient.ReadTextAsync(await admin.Http.GetAsync("/api/admin/reports/export/sales.csv?range=ThisYear"));

        csv.Should().Contain(order.OrderNumber);
        csv.Should().NotContain("+977", "no phone number is exported");
        csv.Should().NotContain("@test.dev", "no email address is exported");
        csv.Should().NotContain("Kathmandu", "no address is exported");
        csv.ToLowerInvariant().Should().NotContain("password");
    }

    /* ============================================================ inventory threshold auditing */

    [Fact]
    public async Task Changing_a_low_stock_threshold_is_recorded_with_both_values()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);
        var (seller, variantId, _) = await FirstSellerVariantAsync();

        var before = await admin.GetAsync<PagedResult<AuditLogResponse>>(
            $"/api/admin/audit-logs?page=1&pageSize=50&action=InventoryThresholdChanged");

        var changed = await seller.PutAsync($"/api/inventory/{variantId}/threshold", new { lowStockThreshold = 42 });
        changed.StatusCode.Should().Be(HttpStatusCode.NoContent, await ApiClient.ReadTextAsync(changed));

        var after = await admin.GetAsync<PagedResult<AuditLogResponse>>(
            $"/api/admin/audit-logs?page=1&pageSize=50&action=InventoryThresholdChanged");

        after!.TotalCount.Should().Be(before!.TotalCount + 1);
        var entry = after.Items.First();
        entry.EntityType.Should().Be("Inventory");
        entry.ChangesJson.Should().Contain("Before").And.Contain("After").And.Contain("42");
    }

    [Fact]
    public async Task Setting_the_threshold_it_already_has_records_nothing()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);
        var (seller, variantId, _) = await FirstSellerVariantAsync();

        await seller.PutAsync($"/api/inventory/{variantId}/threshold", new { lowStockThreshold = 7 });
        var between = await admin.GetAsync<PagedResult<AuditLogResponse>>(
            $"/api/admin/audit-logs?page=1&pageSize=50&action=InventoryThresholdChanged");

        await seller.PutAsync($"/api/inventory/{variantId}/threshold", new { lowStockThreshold = 7 });

        var after = await admin.GetAsync<PagedResult<AuditLogResponse>>(
            $"/api/admin/audit-logs?page=1&pageSize=50&action=InventoryThresholdChanged");
        after!.TotalCount.Should().Be(between!.TotalCount, "nothing changed, so nothing was written");
    }

    /* ================================================================ who may read any of it */

    [Fact]
    public async Task Only_an_administrator_can_read_the_reports_or_the_audit_log()
    {
        var (customer, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.CustomerEmail, MarketplaceTestData.CustomerPassword);
        var (seller, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);

        foreach (var route in ReportRoutes)
        {
            (await _factory.CreateClient().GetAsync(route)).StatusCode
                .Should().Be(HttpStatusCode.Unauthorized, $"{route} must not answer an anonymous caller");
            (await customer.Http.GetAsync(route)).StatusCode
                .Should().Be(HttpStatusCode.Forbidden, $"{route} must not answer a customer");
            (await seller.Http.GetAsync(route)).StatusCode
                .Should().Be(HttpStatusCode.Forbidden, $"{route} must not answer a seller");
        }
    }

    [Fact]
    public async Task An_administrator_can_read_every_report_and_the_audit_log()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        foreach (var route in ReportRoutes)
        {
            (await admin.Http.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.OK, $"an admin may read {route}");
        }
    }

    /* ------------------------------------------------------------------------------ helpers */

    private async Task<int> CountAsync(ApiClient client, string route)
    {
        var response = await client.Http.GetAsync(route);
        response.EnsureSuccessStatusCode();

        var body = await ApiClient.ReadTextAsync(response);

        // Every one of these routes answers the same paged shape, so one reader covers them all.
        var page = System.Text.Json.JsonSerializer.Deserialize<PagedResult<JsonElement>>(body);
        return page?.TotalCount ?? 0;
    }

    private async Task<int> PublicProductSearchAsync(string term)
    {
        var client = new ApiClient(_factory.CreateClient());
        var page = await client.GetAsync<PagedResult<ProductSummaryResponse>>(
            $"/api/products?page=1&pageSize=50&search={Uri.EscapeDataString(term)}");
        return page?.TotalCount ?? 0;
    }

    private static string FlipCase(string value) =>
        string.Concat(value.Select(c => char.IsLetter(c) ? (char)(c ^ 32) : c));

    private sealed record PlacedOrder(ApiClient Client, Guid OrderId, string OrderNumber);

    private sealed record DeliveredOrder(ApiClient Client, Guid OrderId, Guid ProductId);

    private async Task<(ApiClient Client, CheckoutResponse Order)> CheckoutAsync(string? coupon)
    {
        var email = $"reporting-{Guid.NewGuid():N}@test.dev";
        var (client, _) = await AuthHelper.RegisterAsync(_factory, email, MarketplaceTestData.CustomerPassword);

        var address = await client.PostAsync("/api/addresses", new CreateAddressRequest(
            "Home", "Test Customer", "+9779800000000", "12 Ratna Marg", null, "Kathmandu", null, "44600", "NP", true));
        address.StatusCode.Should().Be(HttpStatusCode.Created);

        var addressId = (await ApiClient.ReadAsync<AddressResponse>(address))!.Id;
        var added = await client.PostAsync("/api/cart/items",
            new AddCartItemRequest(_data.SellerAProductId, _data.SellerAProductVariantId, 1));
        added.StatusCode.Should().Be(HttpStatusCode.OK, await ApiClient.ReadTextAsync(added));

        var placed = await client.PostAsync("/api/checkout",
            new CheckoutRequest(addressId, "Mock", coupon, "Phase 16 report order", null, Guid.NewGuid().ToString("N")));
        placed.StatusCode.Should().Be(HttpStatusCode.Created, await ApiClient.ReadTextAsync(placed));

        return (client, (await ApiClient.ReadAsync<CheckoutResponse>(placed))!);
    }

    private async Task<PlacedOrder> CheckoutPlacedAsync(string? coupon = null)
    {
        var (client, order) = await CheckoutAsync(coupon);
        return new PlacedOrder(client, order.OrderId, order.OrderNumber);
    }
    /// <summary>
    /// The one sales-report bucket this order belongs to.
    /// </summary>
    /// <remarks>
    /// Found by computing the bucket the way the report does, rather than by guessing which row
    /// "looks like" it: a year-to-date window buckets by day, so an order placed today sits in
    /// today's bucket.
    /// </remarks>
    private async Task<SalesReportRowResponse?> ReportFor(string orderNumber)
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var placedAt = await context.Orders.AsNoTracking()
            .Where(o => o.OrderNumber == orderNumber)
            .Select(o => (DateTimeOffset?)o.PlacedAt)
            .FirstOrDefaultAsync();

        if (placedAt is null)
        {
            return null;
        }

        var report = await admin.GetAsync<List<SalesReportRowResponse>>("/api/admin/reports/sales?range=ThisYear");

        // Year-to-date buckets by month (DateTimeRange.Resolve pairs ThisYear with a monthly
        // interval), so the bucket an order falls into is identified by its month.
        return report!.FirstOrDefault(r => r.Period.Year == placedAt.Value.Year && r.Period.Month == placedAt.Value.Month);
    }

    private async Task DeliverAsync(PlacedOrder order)
    {
        await WebhookHelper.SettlePaymentAsync(_factory, order.Client, order.OrderId, "evt-p16");

        var (seller, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);

        var sellerOrders = await seller.GetAsync<PagedResult<SellerOrderSummaryResponse>>("/api/seller/orders?page=1&pageSize=50");
        var mine = sellerOrders!.Items.Where(so => so.OrderId == order.OrderId).ToList();
        mine.Should().NotBeEmpty();

        foreach (var sellerOrder in mine)
        {
            foreach (var step in new[]
            {
                SellerOrderStatus.Confirmed, SellerOrderStatus.Processing,
                SellerOrderStatus.Packed, SellerOrderStatus.Shipped, SellerOrderStatus.Delivered
            })
            {
                var response = await seller.PutAsync($"/api/seller/orders/{sellerOrder.Id}/status",
                    new
                    {
                        status = step.ToString(),
                        note = (string?)null,
                        carrierName = step == SellerOrderStatus.Shipped ? "Phase 16 Courier" : (string?)null,
                        trackingNumber = step == SellerOrderStatus.Shipped ? "P16-TRACK" : (string?)null,
                    });
                response.StatusCode.Should().Be(HttpStatusCode.NoContent, await ApiClient.ReadTextAsync(response));
            }
        }
    }

    private async Task<DeliveredOrder> DeliveredOrderAsync()
    {
        var order = await CheckoutPlacedAsync();
        await DeliverAsync(order);
        return new DeliveredOrder(order.Client, order.OrderId, _data.SellerAProductId);
    }

    private async Task<Guid> FirstOrderItemIdAsync(Guid orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        return await context.OrderItems.AsNoTracking().Where(i => i.OrderId == orderId).Select(i => i.Id).FirstAsync();
    }

    private Guid SellerIdFor(PlacedOrder order) => SellerIdFor(order.OrderId);

    private Guid SellerIdFor(DeliveredOrder order) => SellerIdFor(order.OrderId);

    private Guid SellerIdFor(Guid orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        return context.SellerOrders.AsNoTracking().Where(so => so.OrderId == orderId).Select(so => so.SellerId).First();
    }
    /// <summary>
    /// A seller variant worth asserting about: the one holding the most stock that has a SKU.
    /// </summary>
    /// <remarks>
    /// Chosen by "most stock, and identifiable" rather than "the first one", because the class
    /// shares a single database and earlier tests move quantities around. A variant with no SKU
    /// cannot be matched back to a report row, and one another test has emptied is no use to a
    /// test about stock levels.
    /// </remarks>
    private async Task<(ApiClient Client, Guid VariantId, string Sku)> FirstSellerVariantAsync()
    {
        var (seller, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);

        var items = await seller.GetAsync<PagedResult<InventoryItemResponse>>("/api/inventory?page=1&pageSize=50");
        var item = items!.Items
            .Where(i => !string.IsNullOrEmpty(i.Sku))
            .OrderByDescending(i => i.AvailableQuantity)
            .First();

        return (seller, item.ProductVariantId, item.Sku);
    }

/// <summary>The inventory report's own row for one variant, so a test acts on the thing it asserts about.</summary>
private async Task<InventoryReportRowResponse> ReportRowForAsync(ApiClient admin, string sku)
    {
        // A SKU identifies one variant; a quantity does not, because several variants commonly
        // hold the same number of units, and a test that grabbed the wrong one would go on to
        // assert about stock it never touched.
        var report = await admin.GetAsync<PagedResult<InventoryReportRowResponse>>("/api/admin/reports/inventory?page=1&pageSize=100");
        return report!.Items.First(i => i.Sku == sku);
    }

    private static async Task SetThresholdAsync(ApiClient seller, Guid variantId, int threshold)
    {
        var response = await seller.PutAsync($"/api/inventory/{variantId}/threshold", new { lowStockThreshold = threshold });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await ApiClient.ReadTextAsync(response));
    }
}
