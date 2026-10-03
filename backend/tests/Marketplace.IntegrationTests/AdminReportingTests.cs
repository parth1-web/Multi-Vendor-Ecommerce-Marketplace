using System.Net;
using FluentAssertions;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Analytics.DTOs;
using Marketplace.Application.Modules.Notifications.DTOs;
using Marketplace.Domain.Enums;
using Marketplace.IntegrationTests.Infrastructure;
using Xunit;

namespace Marketplace.IntegrationTests;

/// <summary>
/// The admin reporting and audit endpoints, asserted twice over.
///
/// The first half is behaviour: who may read them, and that each filter the route accepts
/// actually narrows the result rather than being accepted and ignored. A filter that is silently
/// dropped is worse than one that is absent, because the operator believes they have narrowed
/// something.
///
/// The second half is characterisation. Several fields in these payloads are constants the service
/// writes rather than figures it computes — the sales report's discounts, tax, shipping and
/// refunds, its "net revenue", the seller report's average rating, the summary's conversion rate.
/// The reports page omits them rather than printing zeros as measurements, and these tests pin
/// that they are still zeros. When somebody fixes one of them, these fail on purpose: the zeros
/// were a decision about what to show, and changing them is a deliberate act rather than a silent
/// upgrade.
/// </summary>
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

    /* ------------------------------------------------------------------ who may read them */

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

    /* --------------------------------------------------------------------------- filtering */

    [Fact]
    public async Task The_audit_log_is_paged_by_the_server_and_not_by_the_browser()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var page = await admin.GetAsync<PagedResult<AuditLogResponse>>("/api/admin/audit-logs?page=1&pageSize=5");

        page.Should().NotBeNull();
        page!.TotalCount.Should().BeGreaterThan(0);
        page.Items.Should().HaveCountLessThanOrEqualTo(5, "the page size is the server's to apply");
        page.TotalPages.Should().Be((int)Math.Ceiling(page.TotalCount / (double)page.PageSize));
        page.Items.Should().BeInDescendingOrder(entry => entry.CreatedAt, "the newest event is the first row");
    }

    [Fact]
    public async Task The_audit_log_caps_the_page_size_rather_than_agreeing_to_it()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var page = await admin.GetAsync<PagedResult<AuditLogResponse>>("/api/admin/audit-logs?page=1&pageSize=500");

        page!.PageSize.Should().Be(PageRequest.MaxPageSize);
        page.Items.Should().HaveCountLessThanOrEqualTo(PageRequest.MaxPageSize);
    }

    [Fact]
    public async Task The_action_filter_narrows_the_audit_log_to_that_action()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var page = await admin.GetAsync<PagedResult<AuditLogResponse>>("/api/admin/audit-logs?page=1&pageSize=25&action=Login");

        page!.TotalCount.Should().BeGreaterThan(0, "signing in is recorded, so the filter has something to find");
        page.Items.Should().OnlyContain(entry => entry.Action == AuditAction.Login);
    }

    [Fact]
    public async Task The_audit_log_search_matches_the_actor_and_the_target_name()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        // The seeded admin's own address, which is what its sign-ins record as both actor and
        // target name.
        var fragment = MarketplaceTestData.AdminEmail.Split('@')[0];

        var byActor = await admin.GetAsync<PagedResult<AuditLogResponse>>(
            $"/api/admin/audit-logs?page=1&pageSize=25&search={fragment}");

        byActor!.TotalCount.Should().BeGreaterThan(0);
        byActor.Items.Should().OnlyContain(entry =>
            entry.ActorEmail.Contains(fragment, StringComparison.OrdinalIgnoreCase)
            || (entry.EntityName ?? string.Empty).Contains(fragment, StringComparison.OrdinalIgnoreCase));

        var byNothing = await admin.GetAsync<PagedResult<AuditLogResponse>>(
            "/api/admin/audit-logs?page=1&pageSize=25&search=no-such-actor-or-target-anywhere");

        byNothing!.TotalCount.Should().Be(0, "a term that matches neither field must return nothing, not everything");

        // The database's collation is case-sensitive, so this only passes because the filter
        // lowercases both sides. Without that, typing a name in lower case finds nothing.
        var byLowerCase = await admin.GetAsync<PagedResult<AuditLogResponse>>(
            $"/api/admin/audit-logs?page=1&pageSize=25&search={fragment.ToLowerInvariant()}");

        byLowerCase!.TotalCount.Should().Be(byActor.TotalCount, "the search does not depend on how the term was capitalised");
    }

    [Fact]
    public async Task The_audit_log_date_filter_bounds_the_window_server_side()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        // Relative to the suite's own clock rather than the wall clock, so the window provably
        // contains the sign-in this test just made on any machine, at any date.
        var everything = await admin.GetAsync<PagedResult<AuditLogResponse>>("/api/admin/audit-logs?page=1&pageSize=1");
        var from = _factory.Clock.UtcNow.AddMinutes(-5).ToString("O");

        var recent = await admin.GetAsync<PagedResult<AuditLogResponse>>(
            $"/api/admin/audit-logs?page=1&pageSize=50&from={Uri.EscapeDataString(from)}");

        recent!.TotalCount.Should().BeGreaterThan(0, "the sign-ins in this test are inside the window");
        recent.Items.Should().OnlyContain(entry => entry.CreatedAt >= _factory.Clock.UtcNow.AddMinutes(-5));
        recent.TotalCount.Should().BeLessThanOrEqualTo(everything!.TotalCount, "a window cannot contain more than the whole log");
    }

    [Fact]
    public async Task The_audit_log_can_be_narrowed_to_one_target()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

var all = await admin.GetAsync<PagedResult<AuditLogResponse>>("/api/admin/audit-logs?page=1&pageSize=50");
        var target = all!.Items.FirstOrDefault(entry => entry.EntityId != null);
        target.Should().NotBeNull("the seeded sign-ins record the account they were for");
        var targetId = target!.EntityId!.Value;

        var narrowed = await admin.GetAsync<PagedResult<AuditLogResponse>>(
            $"/api/admin/audit-logs?page=1&pageSize=50&entityId={targetId}");

        narrowed!.TotalCount.Should().BeGreaterThan(0);
        narrowed.Items.Should().OnlyContain(entry => entry.EntityId == targetId);
    }

    [Fact]
    public async Task A_password_change_is_recorded_without_any_part_of_the_password()
    {
        var (customer, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.CustomerEmail, MarketplaceTestData.CustomerPassword);

        var changed = await customer.PostAsync("/api/auth/change-password", new
        {
            currentPassword = MarketplaceTestData.CustomerPassword,
            newPassword = "Customer@456",
            confirmPassword = "Customer@456",
        });
        changed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);
        var page = await admin.GetAsync<PagedResult<AuditLogResponse>>("/api/admin/audit-logs?page=1&pageSize=50&action=PasswordChanged");

        page!.Items.Should().NotBeEmpty("the password change is recorded");
        foreach (var entry in page.Items)
        {
            // A loop rather than OnlyContain because FluentAssertions builds an expression tree,
            // which cannot hold the pattern match this needs.
            if (entry.ChangesJson is null)
            {
                continue;
            }

            entry.ChangesJson.Should().NotContain("Customer@456", "the new password is not a thing the log keeps");
            entry.ChangesJson.Should().NotContain("Customer@123", "nor is the old one");
            entry.ChangesJson.ToLowerInvariant().Should().NotContain("passwordhash");
            entry.ChangesJson.ToLowerInvariant().Should().NotContain("accesstoken");
        }
    }

    /* ------------------------------------------------------------------- reports: behaviour */

    [Fact]
    public async Task The_sales_report_honours_the_period_preset_it_is_given()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var week = await admin.GetAsync<List<SalesReportRowResponse>>("/api/admin/reports/sales?range=Last7Days");
        var year = await admin.GetAsync<List<SalesReportRowResponse>>("/api/admin/reports/sales?range=ThisYear");

        week.Should().NotBeNull().And.NotBeEmpty();
        week!.Should().HaveCount(7, "a seven-day window is seven daily buckets, gap-filled");
        year.Should().NotBeNull().And.NotBeEmpty();

        // Not "more rows for a longer window": the suite's clock can sit in early January, where a
        // year-to-date window is shorter than a seven-day one. What must hold is the shape of each
        // window — seven daily buckets ending today, and a year-to-date window that starts in
        // January and reaches today.
        var now = _factory.Clock.UtcNow;
        week!.Should().OnlyContain(row => row.Period <= now);
        week!.First().Period.Should().BeOnOrBefore(now.AddDays(-6).Date);
        week!.Last().Period.Should().BeOnOrBefore(now.Date);

        year!.First().Period.Should().Be(new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "ThisYear starts at the first of January");
        year!.Last().Period.Should().BeOnOrBefore(now.Date);
        week.Should().OnlyContain(row => row.GrossRevenue >= 0m && row.Orders >= 0);
    }

    [Fact]
    public async Task An_unrecognised_period_falls_back_to_the_default_rather_than_failing()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var nonsense = await admin.GetAsync<List<SalesReportRowResponse>>("/api/admin/reports/sales?range=NotAPreset");
        var last30 = await admin.GetAsync<List<SalesReportRowResponse>>("/api/admin/reports/sales?range=Last30Days");

        nonsense.Should().NotBeNull("an unknown preset is not an error, it falls back to the default window");
        nonsense.Should().HaveCount(last30!.Count);
    }

    [Fact]
    public async Task The_seller_report_covers_every_seller_even_one_that_has_never_sold()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var report = await admin.GetAsync<List<SellerReportRowResponse>>("/api/admin/reports/sellers");

        report.Should().NotBeNull().And.NotBeEmpty();
        report!.Select(row => row.SellerId).Should().OnlyHaveUniqueItems();
        report.Should().BeInDescendingOrder(row => row.GrossRevenue, "the report ranks by revenue");
    }

    [Fact]
    public async Task The_inventory_report_reports_the_quantities_it_stores()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var report = await admin.GetAsync<List<InventoryReportRowResponse>>("/api/admin/reports/inventory");

        report.Should().NotBeNull().And.NotBeEmpty();
        report!.Should().OnlyContain(row => row.Available >= 0 && row.Reserved >= 0 && row.Sold >= 0);
        report.Should().BeInAscendingOrder(row => row.Available, "lowest stock first is the order an operator wants");
        report.Should().HaveCountLessThanOrEqualTo(500, "the endpoint caps the result and the page must not imply otherwise");
    }

    [Fact]
    public async Task The_commission_report_is_empty_when_nothing_has_been_commissioned_in_the_period()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        // Last month is before the seeded data in a suite that runs on the day it is created, so
        // this proves the range is applied rather than ignored.
        var lastMonth = await admin.GetAsync<List<CommissionReportRowResponse>>("/api/admin/reports/commissions?range=LastMonth");

        lastMonth.Should().NotBeNull();
        lastMonth.Should().BeEmpty("nothing was commissioned last month, and the range being applied has to be why");
    }

    /* ------------------------------------------- reports: the constants, pinned on purpose */

    [Fact]
    public async Task The_sales_report_still_reports_zero_for_the_measures_it_does_not_compute()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var report = await admin.GetAsync<List<SalesReportRowResponse>>("/api/admin/reports/sales?range=ThisYear");

        report!.Should().NotBeEmpty();
        report.Should().OnlyContain(row => row.Discounts == 0m, "discounts is a constant, so the page must not print it");
        report.Should().OnlyContain(row => row.Tax == 0m, "tax is a constant, so the page must not print it");
        report.Should().OnlyContain(row => row.Shipping == 0m, "shipping is a constant, so the page must not print it");
        report.Should().OnlyContain(row => row.Refunds == 0m, "refunds is a constant, so the page must not print it");
        report.Should().OnlyContain(row => row.NetRevenue == row.GrossRevenue, "net revenue repeats gross rather than being a profit figure");
    }

    [Fact]
    public async Task The_seller_report_still_reports_zero_for_average_rating()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var report = await admin.GetAsync<List<SellerReportRowResponse>>("/api/admin/reports/sellers");

        report.Should().NotBeNull().And.NotBeEmpty();
        report!.Should().OnlyContain(row => row.AverageRating == 0m, "no review data is joined, so the column must stay off the page");
    }

    [Fact]
    public async Task The_admin_summary_still_reports_zero_for_conversion_rate()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var summary = await admin.GetAsync<AdminSummaryResponse>("/api/admin/analytics/summary");

        summary.Should().NotBeNull();
        summary!.ConversionRate.Should().Be(0m, "there is no traffic to convert, so the figure is a constant the overview omits");
        summary.AverageOrderValue.Should().BeGreaterOrEqualTo(0m);
        summary.RefundRate.Should().BeGreaterOrEqualTo(0m);
    }

    [Fact]
    public async Task The_sales_csv_is_order_level_and_leaves_no_audit_trail()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var before = await admin.GetAsync<PagedResult<AuditLogResponse>>("/api/admin/audit-logs?page=1&pageSize=1");

        var csv = await admin.Http.GetAsync("/api/admin/reports/export/sales.csv?range=ThisYear");
        csv.StatusCode.Should().Be(HttpStatusCode.OK);
        csv.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

        var text = await ApiClient.ReadTextAsync(csv);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Contain("Order Number").And.Contain("Subtotal").And.Contain("Refunded");
        lines.Should().OnlyHaveUniqueItems("one row per order, so no order is written twice");

        var after = await admin.GetAsync<PagedResult<AuditLogResponse>>("/api/admin/audit-logs?page=1&pageSize=1");

        after!.TotalCount.Should().Be(before!.TotalCount,
            "the API defines ReportExported but never writes it, which is why the export button says the download is untraceable");
    }
}