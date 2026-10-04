using System.Text;
using Marketplace.API.Middleware;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Modules.Analytics.Abstractions;
using Marketplace.Application.Modules.Analytics.DTOs;
using Marketplace.Application.Modules.Notifications.Abstractions;
using Marketplace.Application.Modules.Notifications.DTOs;
using Marketplace.Application.Modules.Payments.Abstractions;
using Marketplace.Application.Modules.Payments.DTOs;
using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Marketplace.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.API.Controllers;

/// <summary>Seller analytics and earnings.</summary>
[ApiController]
[Route("api/seller")]
[Authorize(Policy = Security.AuthorizationPolicies.SellerOnly)]
public sealed class SellerDashboardController(
    ISellerAnalyticsService analytics,
    ICommissionService commissions,
    IClock clock) : ControllerBase
{
    private static readonly DateTimePreset[] AllowedPresets =
    [
        DateTimePreset.Last7Days, DateTimePreset.Last30Days, DateTimePreset.Last90Days,
        DateTimePreset.ThisMonth, DateTimePreset.LastMonth, DateTimePreset.ThisYear, DateTimePreset.Custom
    ];

    [HttpGet("analytics/summary")]
    [ProducesResponseType(typeof(SellerSummaryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken) => Ok(await analytics.GetSummaryAsync(cancellationToken));

    [HttpGet("analytics/revenue")]
    [ProducesResponseType(typeof(IReadOnlyList<RevenuePointResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Revenue([FromQuery] string? range, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken cancellationToken) =>
        Ok(await analytics.GetRevenueAsync(Resolve(range, from, to, clock), cancellationToken));

    [HttpGet("analytics/top-products")]
    [ProducesResponseType(typeof(IReadOnlyList<TopProductResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> TopProducts([FromQuery] string? range, [FromQuery] int take = 10, CancellationToken cancellationToken = default) =>
        Ok(await analytics.GetTopProductsAsync(Resolve(range, null, null, clock), take, cancellationToken));

    [HttpGet("analytics/sales-by-category")]
    [ProducesResponseType(typeof(IReadOnlyList<CategorySalesResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SalesByCategory([FromQuery] string? range, CancellationToken cancellationToken) =>
        Ok(await analytics.GetSalesByCategoryAsync(Resolve(range, null, null, clock), cancellationToken));

    [HttpGet("analytics/orders")]
    [ProducesResponseType(typeof(IReadOnlyList<OrderStatusCountResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> OrderBreakdown(CancellationToken cancellationToken) =>
        Ok(await analytics.GetOrderStatusBreakdownAsync(cancellationToken));

    [HttpGet("commissions")]
    [ProducesResponseType(typeof(Microsoft.AspNetCore.Mvc.IActionResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Commissions([FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? status, CancellationToken cancellationToken) =>
        Ok(await commissions.ListOwnAsync(page, pageSize, Enum.TryParse<CommissionStatus>(status, true, out var parsed) ? parsed : null, cancellationToken));

    [HttpGet("payouts")]
    public async Task<IActionResult> Payouts([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await commissions.ListPayoutsAsync(page, pageSize, cancellationToken));

    internal static DateTimeRange Resolve(string? range, DateTimeOffset? from, DateTimeOffset? to, IClock clock)
    {
        var preset = Enum.TryParse<DateTimePreset>(range, true, out var parsed) ? parsed : DateTimePreset.Last30Days;
        return DateTimeRange.Resolve(preset, from, to, clock.UtcNow, AllowedPresets);
    }
}

/// <summary>Admin analytics, reports, audit log and user administration.</summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = Security.AuthorizationPolicies.AdminOnly)]
public sealed class AdminDashboardController(
    IAdminAnalyticsService analytics,
    IReportService reports,
    IAuditQueryService audit,
    IRepository<User> users,
    ICurrentUser currentUser,
    IAuditService auditService,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<AdminDashboardController> logger) : ControllerBase
{
    private static readonly DateTimePreset[] AllowedPresets =
    [
        DateTimePreset.Last7Days, DateTimePreset.Last30Days, DateTimePreset.Last90Days,
        DateTimePreset.ThisMonth, DateTimePreset.LastMonth, DateTimePreset.ThisYear, DateTimePreset.Custom
    ];

    [HttpGet("analytics/summary")]
    [ProducesResponseType(typeof(AdminSummaryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken) => Ok(await analytics.GetSummaryAsync(cancellationToken));

    [HttpGet("analytics/revenue")]
    [ProducesResponseType(typeof(IReadOnlyList<RevenuePointResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Revenue([FromQuery] string? range, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken cancellationToken) =>
        Ok(await analytics.GetRevenueAsync(Range(range, from, to), cancellationToken));

    [HttpGet("analytics/growth")]
    [ProducesResponseType(typeof(IReadOnlyList<GrowthPointResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Growth([FromQuery] string? range, CancellationToken cancellationToken) =>
        Ok(await analytics.GetGrowthAsync(Range(range, null, null), cancellationToken));

    [HttpGet("analytics/category-performance")]
    [ProducesResponseType(typeof(IReadOnlyList<CategorySalesResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Categories([FromQuery] string? range, CancellationToken cancellationToken) =>
        Ok(await analytics.GetCategoryPerformanceAsync(Range(range, null, null), cancellationToken));

    [HttpGet("analytics/refunds")]
    [ProducesResponseType(typeof(RefundAnalyticsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Refunds([FromQuery] string? range, CancellationToken cancellationToken) =>
        Ok(await analytics.GetRefundAnalyticsAsync(Range(range, null, null), cancellationToken));

[HttpGet("reports/sales")]
    public async Task<IActionResult> SalesReport([FromQuery] string? range, CancellationToken cancellationToken) =>
        Ok(await reports.SalesAsync(Range(range, null, null), cancellationToken));

    /// <summary>
    /// Sellers, ranked by revenue, paged and filtered.
    /// </summary>
    /// <remarks>
    /// <c>range</c> is optional here and its absence is meaningful: with no period the order
    /// aggregates are lifetime totals, which is what this report has always shown. Supplying one
    /// scopes them to that window. Both are real answers and the caller has to say which it wants.
    /// </remarks>
    [HttpGet("reports/sellers")]
    public async Task<IActionResult> SellerReport(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] string? range,
        CancellationToken cancellationToken)
    {
        SellerStatus? parsedStatus = Enum.TryParse<SellerStatus>(status, true, out var statusValue) ? statusValue : null;
        var window = string.IsNullOrWhiteSpace(range) ? null : Range(range, null, null);

        return Ok(await reports.SellersAsync(
            new SellerReportQuery(page, pageSize, search, parsedStatus, window),
            cancellationToken));
    }

    /// <summary>
    /// Stock per variant, lowest available first, paged.
    /// </summary>
    /// <remarks>
    /// The low-stock and out-of-stock filters use the same definitions as the seller inventory
    /// screen — sellable quantity against the variant's own threshold, and available quantity of
    /// zero or less — so a row that this report calls low stock is the row the seller sees as low
    /// stock.
    /// </remarks>
    [HttpGet("reports/inventory")]
    public async Task<IActionResult> InventoryReport(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        [FromQuery] bool? lowStockOnly,
        [FromQuery] bool? outOfStockOnly,
        [FromQuery] Guid? sellerId,
        CancellationToken cancellationToken) =>
        Ok(await reports.InventoryAsync(
            new InventoryReportQuery(page, pageSize, search, lowStockOnly ?? false, outOfStockOnly ?? false, sellerId),
            cancellationToken));

    [HttpGet("reports/commissions")]
    public async Task<IActionResult> CommissionReport([FromQuery] string? range, CancellationToken cancellationToken) =>
        Ok(await reports.CommissionsAsync(Range(range, null, null), cancellationToken));

    /// <summary>
    /// The sales CSV, streamed row by row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written straight to the response instead of being built as one string, so a year of orders
    /// does not have to exist in memory to be exported. The row cap is part of the contract: past
    /// it the export stops and says so in <c>X-Export-Truncated</c> and in the audit record,
    /// rather than running until the request is killed.
    /// </para>
    /// <para>
    /// Every export is recorded as <see cref="AuditAction.ReportExported"/> with the report name,
    /// the window, the row count and whether it was cut short — the facts somebody needs when
    /// asking later who took the data and how much of it. Nothing about the orders themselves goes
    /// into the record.
    /// </para>
    /// <para>
    /// The window is the same <c>range</c> preset the sales report takes, resolved by the same
    /// code, so "last 30 days" on screen and in the download are the same thirty days.
    /// </para>
    /// </remarks>
    [HttpGet("reports/export/sales.csv")]
    public async Task ExportSales([FromQuery] string? range, CancellationToken cancellationToken)
    {
        var window = Range(range, null, null);

        // Every header is set before the first byte is written. Once the body has started the
        // response is committed and no further header can be added, which is why the row count is
        // not one of them: it is not knowable until the rows have been written, and a header that
        // is silently missing is worse than no header at all. The count and whether the cap was
        // reached both go into the audit record, and a file that hit the cap is exactly
        // IReportService.SalesExportRowLimit lines long.
        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition = $"attachment; filename=\"sales-{DateTime.UtcNow:yyyyMMdd}.csv\"";
        Response.Headers["X-Export-Row-Limit"] = IReportService.SalesExportRowLimit.ToString(System.Globalization.CultureInfo.InvariantCulture);

        await using var writer = new StreamWriter(Response.Body, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteLineAsync("Order Number,Date,Status,Subtotal,Discount,Shipping,Tax,Total,Refunded,Items,Sellers");

        var written = 0;

        await foreach (var row in reports.StreamSalesAsync(window, cancellationToken))
        {
            var line = new StringBuilder()
                .Append(Escape(row.OrderNumber)).Append(',')
                .Append(row.PlacedAt.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape(row.Status)).Append(',')
                .Append(row.Subtotal.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Discount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Shipping.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Tax.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Total.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Refunded.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(row.ItemCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(row.SellerCount.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append('\n');

            await writer.WriteAsync(line).ConfigureAwait(false);
            written++;
        }

        await writer.FlushAsync(cancellationToken);

        // A file with exactly the cap's worth of rows may have been cut short; the count is what
        // tells the reader whether it was.
        var truncated = written >= IReportService.SalesExportRowLimit;

        await auditService.RecordAsync(
            AuditAction.ReportExported,
            "SalesReport",
            null,
            $"sales-{range ?? "Last30Days"}",
            new
            {
                report = "sales",
                requestedRange = range ?? "Last30Days",
                from = window.From,
                to = window.To,
                rowCount = written,
                rowLimit = IReportService.SalesExportRowLimit,
                truncated
            },
            cancellationToken);

        logger.LogInformation(
            "Sales CSV exported for {From:o} to {To:o}: {RowCount} rows{Truncated}",
            window.From,
            window.To,
            written,
            truncated ? " (at the row limit, so the export may be incomplete)" : string.Empty);
    }

    /// <summary>
    /// Quotes a CSV field that might contain a comma, a quote or a newline.
    /// </summary>
    /// <remarks>
    /// Order numbers and statuses cannot contain those today, but a status name that gained a
    /// comma in a future enum, or a reference that gained a quote, would otherwise produce a file
    /// that silently disagrees with itself about which column is which.
    /// </remarks>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"")}\"";
    }

    [HttpGet("audit-logs")]
    [ProducesResponseType(typeof(Microsoft.AspNetCore.Mvc.IActionResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> AuditLogs(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? action,
        [FromQuery] string? entityType, [FromQuery] Guid? entityId, [FromQuery] Guid? actorId,
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] string? search,
        CancellationToken cancellationToken) =>
        Ok(await audit.ListAsync(new AuditListQuery(
            page, pageSize,
            Enum.TryParse<AuditAction>(action, true, out var parsed) ? parsed : null,
            entityType, entityId, actorId, from, to, search), cancellationToken));

    [HttpGet("users")]
    [ProducesResponseType(typeof(Microsoft.AspNetCore.Mvc.IActionResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Users([FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, [FromQuery] string? role, CancellationToken cancellationToken)
    {
        var paging = new Marketplace.Application.Common.Models.PageRequest(page, pageSize);
        var source = users.Query().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = SearchPattern.Contains(search);
            source = source.Where(u => EF.Functions.Like(u.Email.ToLower(), term) || EF.Functions.Like(u.FirstName.ToLower(), term) || EF.Functions.Like(u.LastName.ToLower(), term));
        }

        if (Enum.TryParse<UserRole>(role, true, out var parsedRole))
        {
            source = source.Where(u => u.Role == parsedRole);
        }

        var all = await source.OrderByDescending(u => u.CreatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
        var total = all.Count;

        var pageItems = all.Skip(paging.Skip).Take(paging.PageSize)
            .Select(u => new AdminUserResponse(u.Id, u.Email, u.FullName, u.Role, u.IsActive, u.IsEmailConfirmed, u.CreatedAt, u.LastLoginAt, null))
            .ToList();

        return Ok(new Marketplace.Application.Common.Models.PagedResult<AdminUserResponse>(pageItems, paging.Page, paging.PageSize, total));
    }

    /// <summary>
    /// Changes an account's role.
    /// </summary>
    /// <remarks>
    /// Two rules, both about privilege rather than about convenience.
    ///
    /// A caller may not grant a role above their own: only a super administrator may create or
    /// promote to <see cref="UserRole.SuperAdmin"/>. Without this a plain administrator could
    /// mint an account more powerful than itself and then use it — the privilege-escalation path
    /// the role hierarchy exists to prevent.
    ///
    /// And nobody may change their own role here. An administrator demoting themselves locks
    /// themselves out mid-session; an administrator promoting themselves is escalation by another
    /// route. Someone else's account is the only thing this endpoint is for.
    /// </remarks>
    [HttpPut("users/{id:guid}/role")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ChangeRole(Guid id, [FromBody] ChangeUserRoleRequest request, CancellationToken cancellationToken)
    {
        var callerIsSuperAdmin = currentUser.IsInRole(UserRole.SuperAdmin);

        if (request.Role == UserRole.SuperAdmin && !callerIsSuperAdmin)
        {
            return Forbid();
        }

        if (id == currentUser.UserId)
        {
            return Forbid();
        }

        var user = await users.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        var previous = user.Role;
        user.ChangeRole(request.Role, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(AuditAction.UserRoleChanged, nameof(User), user.Id, user.Email,
            new { From = previous.ToString(), To = request.Role.ToString() }, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Enables or disables an account.
    /// </summary>
    /// <remarks>
    /// Self-disabling is refused for the same reason self-demotion is: it is almost always a
    /// mistake, and it can leave a marketplace with no administrator able to reach this endpoint.
    /// </remarks>
    [HttpPut("users/{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeUserStatusRequest request, CancellationToken cancellationToken)
    {
        if (id == currentUser.UserId)
        {
            return Forbid();
        }

        var user = await users.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        user.SetActive(request.IsActive, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(AuditAction.UserStatusChanged, nameof(User), user.Id, user.Email,
            new { user.IsActive }, cancellationToken);
        return NoContent();
    }

    private DateTimeRange Range(string? range, DateTimeOffset? from, DateTimeOffset? to)
    {
        var preset = Enum.TryParse<DateTimePreset>(range, true, out var parsed) ? parsed : DateTimePreset.Last30Days;
        return DateTimeRange.Resolve(preset, from, to, clock.UtcNow, AllowedPresets);
    }
}

public sealed record AdminUserResponse(
    Guid Id, string Email, string FullName, UserRole Role, bool IsActive,
    bool IsEmailConfirmed, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt, Guid? SellerId);

public sealed record ChangeUserRoleRequest(UserRole Role);

public sealed record ChangeUserStatusRequest(bool IsActive);

/// <summary>Customer dashboard summary.</summary>
[ApiController]
[Route("api/customer/analytics")]
[Authorize]
public sealed class CustomerDashboardController(ICustomerAnalyticsService analytics) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType(typeof(CustomerSummaryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken) => Ok(await analytics.GetSummaryAsync(cancellationToken));
}
