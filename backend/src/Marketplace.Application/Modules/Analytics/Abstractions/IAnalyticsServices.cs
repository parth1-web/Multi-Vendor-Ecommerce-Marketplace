using Marketplace.Domain.Enums;
using Marketplace.Application.Modules.Analytics.DTOs;
using Marketplace.Application.Modules.Orders.DTOs;

namespace Marketplace.Application.Modules.Analytics.Abstractions;

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

public interface IReportService
{
    Task<IReadOnlyList<SalesReportRowResponse>> SalesAsync(DateTimeRange range, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SellerReportRowResponse>> SellersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryReportRowResponse>> InventoryAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommissionReportRowResponse>> CommissionsAsync(DateTimeRange range, CancellationToken cancellationToken = default);

    Task<string> ExportSalesCsvAsync(DateTimeRange range, CancellationToken cancellationToken = default);
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
