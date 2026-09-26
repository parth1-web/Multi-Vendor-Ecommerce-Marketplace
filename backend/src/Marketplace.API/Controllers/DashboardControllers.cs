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
    IUnitOfWork unitOfWork,
    IClock clock) : ControllerBase
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

    [HttpGet("reports/sellers")]
    public async Task<IActionResult> SellerReport(CancellationToken cancellationToken) => Ok(await reports.SellersAsync(cancellationToken));

    [HttpGet("reports/inventory")]
    public async Task<IActionResult> InventoryReport(CancellationToken cancellationToken) => Ok(await reports.InventoryAsync(cancellationToken));

    [HttpGet("reports/commissions")]
    public async Task<IActionResult> CommissionReport([FromQuery] string? range, CancellationToken cancellationToken) =>
        Ok(await reports.CommissionsAsync(Range(range, null, null), cancellationToken));

    [HttpGet("reports/export/sales.csv")]
    public async Task<IActionResult> ExportSales([FromQuery] string? range, CancellationToken cancellationToken)
    {
        var csv = await reports.ExportSalesCsvAsync(Range(range, null, null), cancellationToken);
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"sales-{DateTime.UtcNow:yyyyMMdd}.csv");
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
            var term = $"%{search.Trim()}%";
            source = source.Where(u => EF.Functions.Like(u.Email, term) || EF.Functions.Like(u.FirstName, term) || EF.Functions.Like(u.LastName, term));
        }

        if (Enum.TryParse<UserRole>(role, true, out var parsedRole))
        {
            source = source.Where(u => u.Role == parsedRole);
        }

        var result = await source
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync(cancellationToken)
            .ContinueWith(t => new Marketplace.Application.Common.Models.PagedResult<AdminUserResponse>(
                t.Result.Skip(paging.Skip).Take(paging.PageSize)
                    .Select(u => new AdminUserResponse(u.Id, u.Email, u.FullName, u.Role, u.IsActive, u.IsEmailConfirmed, u.CreatedAt, u.LastLoginAt, u.SellerId))
                    .ToList(), paging.Page, paging.PageSize, t.Result.Count), cancellationToken, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default)
            .ConfigureAwait(false);

        return Ok(result);
    }

    [HttpPut("users/{id:guid}/role")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangeRole(Guid id, [FromBody] ChangeUserRoleRequest request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        user.ChangeRole(request.Role, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPut("users/{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeUserStatusRequest request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        user.SetActive(request.IsActive, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
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
