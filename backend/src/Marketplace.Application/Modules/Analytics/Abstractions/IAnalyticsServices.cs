using Marketplace.Domain.Enums;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Analytics.DTOs;
using Marketplace.Application.Modules.Orders.DTOs;

namespace Marketplace.Application.Modules.Analytics.Abstractions;

/// <summary>
/// Filters for the seller report.
/// </summary>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Rows per page, capped by <see cref="PageRequest"/>.</param>
/// <param name="Search">Matches the seller's business name or their store's name.</param>
/// <param name="Status">Restrict to one seller status.</param>
/// <param name="Range">
/// When present, the order aggregates cover this window. When null they are lifetime totals —
/// the behaviour this report had before it grew filters, kept so that "no period" and "a period"
/// cannot be confused for one another.
/// </param>
public sealed record SellerReportQuery(
    int? Page,
    int? PageSize,
    string? Search,
    SellerStatus? Status,
    DateTimeRange? Range);

/// <summary>
/// Filters for the inventory report.
/// </summary>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Rows per page, capped by <see cref="PageRequest"/>.</param>
/// <param name="Search">Matches the product name, the variant SKU, or the store name.</param>
/// <param name="LowStockOnly">Only variants whose sellable quantity is at or below their threshold.</param>
/// <param name="OutOfStockOnly">Only variants with no available quantity.</param>
/// <param name="SellerId">Restrict to one seller.</param>
public sealed record InventoryReportQuery(
    int? Page,
    int? PageSize,
    string? Search,
    bool LowStockOnly,
    bool OutOfStockOnly,
    Guid? SellerId);

public interface ISellerAnalyticsService
{
    Task<SellerSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RevenuePointResponse>> GetRevenueAsync(DateTimeRange range, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TopProductResponse>> GetTopProductsAsync(DateTimeRange range, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CategorySalesResponse>> GetSalesByCategoryAsync(DateTimeRange range, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderStatusCountResponse>> GetOrderStatusBreakdownAsync(CancellationToken cancellationToken = default);
}

public interface IAdminAnalyticsService
{
    Task<AdminSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RevenuePointResponse>> GetRevenueAsync(DateTimeRange range, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GrowthPointResponse>> GetGrowthAsync(DateTimeRange range, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CategorySalesResponse>> GetCategoryPerformanceAsync(DateTimeRange range, CancellationToken cancellationToken = default);

    Task<RefundAnalyticsResponse> GetRefundAnalyticsAsync(DateTimeRange range, CancellationToken cancellationToken = default);
}

public interface ICustomerAnalyticsService
{
    Task<CustomerSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Filters for the seller-facing inventory report.
/// </summary>
/// <remarks>
/// Deliberately has no seller field. A seller reading this report gets their own stock and cannot
/// ask for anybody else's, so the identity comes from the caller's principal rather than from a
/// query string — the one thing that must never be client-supplied on this route.
/// </remarks>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Rows per page, capped by <see cref="PageRequest"/>.</param>
/// <param name="Search">Matches the product name or the variant SKU, case-insensitively.</param>
/// <param name="LowStockOnly">Only variants whose sellable quantity is at or below their threshold.</param>
/// <param name="OutOfStockOnly">Only variants with no available quantity.</param>
public sealed record SellerInventoryReportQuery(
    int? Page,
    int? PageSize,
    string? Search,
    bool LowStockOnly,
    bool OutOfStockOnly);

/// <summary>
/// The admin reporting endpoints. Every read here is filtered, paged and aggregated by the
/// database; none of them loads the underlying table to count or sort it in memory.
/// </summary>
public interface IReportService
{
    Task<IReadOnlyList<SalesReportRowResponse>> SalesAsync(DateTimeRange range, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sellers, paged and filtered. <paramref name="query"/> may carry a period, in which case the
    /// order aggregates cover that window instead of all time.
    /// </summary>
    Task<PagedResult<SellerReportRowResponse>> SellersAsync(SellerReportQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stock per variant, paged, lowest available first.
    /// </summary>
    /// <remarks>
    /// "Low stock" and "out of stock" mean exactly what they mean in the seller inventory screen:
    /// sellable quantity (available less reserved) at or below the variant's own threshold, and
    /// available quantity of zero or less respectively. No fixed number is involved.
    /// </remarks>
    Task<PagedResult<InventoryReportRowResponse>> InventoryAsync(InventoryReportQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommissionReportRowResponse>> CommissionsAsync(DateTimeRange range, CancellationToken cancellationToken = default);

    /// <summary>
    /// The sales CSV, streamed.
    /// </summary>
    /// <remarks>
    /// An async sequence rather than a finished string so that a wide range cannot pull every
    /// matching order into memory before the first byte is written. The caller writes the header,
    /// then each row, and stops at <see cref="SalesExportRowLimit"/> rows — the cap is part of the
    /// contract so that "unbounded" is never true of this endpoint again.
    /// </remarks>
    IAsyncEnumerable<SalesExportRow> StreamSalesAsync(DateTimeRange range, CancellationToken cancellationToken = default);

    /// <summary>
    /// One seller's stock, for the seller themselves.
    /// </summary>
    /// <remarks>
    /// The same rows, filters and ordering as <see cref="InventoryAsync"/>, narrowed to one seller —
    /// and the seller is an argument, not something read from the query string, so a caller cannot
    /// widen this by editing a URL.
    /// </remarks>
    Task<PagedResult<InventoryReportRowResponse>> SellerInventoryAsync(
        Guid sellerId,
        SellerInventoryReportQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The most rows one export will write. Beyond this the response is truncated and says so, in
    /// a header and in the audit record, rather than running until the request dies.
    /// </summary>
    const int SalesExportRowLimit = 50_000;
}

/// <summary>Resolved date window. Always computed server-side; the client cannot widen it.</summary>
public sealed record DateTimeRange(DateTimeOffset From, DateTimeOffset To, AnalyticsInterval Interval)
{
    public static DateTimeRange Resolve(DateTimePreset preset, DateTimeOffset? from, DateTimeOffset? to, DateTimeOffset now, IReadOnlyList<DateTimePreset> allowed)
    {
        var resolved = allowed.Contains(preset) ? preset : DateTimePreset.Last30Days;

        var (start, end, interval) = resolved switch
        {
            // A day-interval window is inclusive of today, so "last 7 days" is seven buckets,
            // not eight: a chart that contradicts its own label is worse than no chart.
            DateTimePreset.Last7Days => (now.AddDays(-6), now, AnalyticsInterval.Day),
            DateTimePreset.Last30Days => (now.AddDays(-29), now, AnalyticsInterval.Day),
            DateTimePreset.Last90Days => (now.AddDays(-90), now, AnalyticsInterval.Week),
            DateTimePreset.ThisMonth => (new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero), now, AnalyticsInterval.Day),
            DateTimePreset.LastMonth => (MonthStart(now.AddMonths(-1)), MonthStart(now), AnalyticsInterval.Day),
            DateTimePreset.ThisYear => (new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, TimeSpan.Zero), now, AnalyticsInterval.Month),
            _ => (from ?? now.AddDays(-30), to ?? now, AnalyticsInterval.Day)
        };

        // Guard rails: never allow an unbounded range.
        if (start < now.AddYears(-3))
        {
            start = now.AddYears(-3);
        }

        if (end > now)
        {
            end = now;
        }

        return new DateTimeRange(start, end, interval);
    }

    private static DateTimeOffset MonthStart(DateTimeOffset value) =>
        new(value.Year, value.Month, 1, 0, 0, 0, TimeSpan.Zero);
}

public enum DateTimePreset
{
    Last7Days = 0,
    Last30Days = 1,
    Last90Days = 2,
    ThisMonth = 3,
    LastMonth = 4,
    ThisYear = 5,
    Custom = 6
}
