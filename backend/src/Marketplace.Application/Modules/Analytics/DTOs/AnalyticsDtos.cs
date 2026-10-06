using Marketplace.Domain.Enums;
using Marketplace.Application.Modules.Orders.DTOs;
namespace Marketplace.Application.Modules.Analytics.DTOs;

public sealed record SellerSummaryResponse(
    decimal TotalSales,
    decimal TodaySales,
    decimal MonthSales,
    decimal PendingEarnings,
    decimal PaidEarnings,
    decimal CommissionPaid,
    int TotalOrders,
    int PendingOrders,
    int ProcessingOrders,
    int ShippedOrders,
    int CompletedOrders,
    int CancelledOrders,
    int TotalProducts,
    int PublishedProducts,
    int PendingApprovalProducts,
    int LowStockProducts,
    int OutOfStockProducts,
    decimal AverageRating,
int ReviewCount,
    int UnansweredReviews,
    decimal AverageOrderValue,
    DateTimeOffset? LastOrderAt);

public sealed record RevenuePointResponse(DateTimeOffset Period, decimal Revenue, decimal Commission, decimal NetEarnings, int OrderCount, int ItemCount);

public sealed record TopProductResponse(Guid ProductId, string Name, string? ImageUrl, int QuantitySold, decimal Revenue, decimal Commission);

public sealed record CategorySalesResponse(Guid CategoryId, string Name, string Slug, int QuantitySold, decimal Revenue, decimal Share);

public sealed record OrderStatusCountResponse(OrderStatus Status, int Count, decimal Total);

public sealed record AdminSummaryResponse(
    int TotalCustomers,
    int TotalSellers,
    int ActiveSellers,
    int PendingSellers,
    int SuspendedSellers,
    int TotalProducts,
    int PublishedProducts,
    int PendingProducts,
    int TotalOrders,
    int PendingOrders,
    int OpenRefunds,
    decimal TotalRevenue,
    decimal CommissionRevenue,
    decimal SellerPayouts,
    decimal RefundedAmount,
decimal RefundRate,
    decimal AverageOrderValue,
    int NewCustomersThisMonth,
    int NewSellersThisMonth,
    int OrdersToday,
    decimal RevenueToday,
    DateTimeOffset GeneratedAt);

public sealed record GrowthPointResponse(DateTimeOffset Period, int Customers, int Sellers, int Products, int Orders);

public sealed record RefundAnalyticsResponse(int TotalRequests, int Approved, int Rejected, int Pending, decimal Amount, decimal Rate);

public sealed record CustomerSummaryResponse(
    int TotalOrders,
    decimal TotalSpent,
    decimal AverageOrderValue,
    int WishlistCount,
    int AddressCount,
    int UnreadNotifications,
    int DeliveredOrders,
    int PendingOrders,
    DateTimeOffset? LastOrderAt,
    IReadOnlyList<RecentOrderSummaryResponse> RecentOrders);

public sealed record RecentOrderSummaryResponse(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset PlacedAt,
    int ItemCount);

/// <summary>
/// One period of the sales report.
/// </summary>
/// <remarks>
/// Every money column is a sum of a value the checkout already persisted on the order, so the
/// report reads history rather than recomputing it from today's configuration:
///
/// <list type="bullet">
/// <item><description><b>GrossRevenue</b> — sum of <c>Order.Subtotal</c>: the catalogue value of
/// the goods sold before any discount. Excludes cancelled orders.</description></item>
/// <item><description><b>Discounts</b> — sum of <c>Order.DiscountAmount</c>: the coupon discount
/// allocated at checkout. Persisted per order, so editing or deleting the coupon later cannot
/// change a figure already reported.</description></item>
/// <item><description><b>Shipping</b> — sum of <c>Order.ShippingAmount</c>: what was charged for
/// delivery, already net of the free-shipping threshold.</description></item>
/// <item><description><b>Tax</b> — sum of <c>Order.TaxAmount</c>: the tax computed at checkout
/// from the rate in force then.</description></item>
/// <item><description><b>Refunds</b> — sum of <c>Order.RefundedAmount</c>: money that has
/// actually been returned. Requests that were rejected or are still under review are not on this
/// column, because they have not been returned.</description></item>
/// <item><description><b>NetRevenue</b> — gross less discounts less refunds: the value of goods
/// actually retained. Shipping and tax are deliberately excluded. Whether collected tax is
/// platform revenue, and whether shipping is income or a pass-through to a carrier, are policy
/// questions this codebase has not answered, and a report that quietly decided them would be
/// making them up. They are reported in their own columns instead.</description></item>
/// </list>
///
/// <b>Commission</b> and <b>NetToSellers</b> come from the seller orders, not from this order's
/// total, because a marketplace order is split per store and only the split is attributable.
/// </remarks>
public sealed record SalesReportRowResponse(
    DateTimeOffset Period,
    int Orders,
    decimal GrossRevenue,
    decimal Discounts,
    decimal Commission,
    decimal NetToSellers,
    decimal Tax,
    decimal Shipping,
    decimal Refunds,
    decimal NetRevenue);

/// <summary>
/// One seller in the seller report.
/// </summary>
/// <remarks>
/// <b>AverageRating</b> is the mean of that seller's <i>visible</i> reviews — the same reviews a
/// shopper can see, because a hidden review is not one anybody can fairly hold against a store.
/// Reviews deleted outright are rows that no longer exist and cannot be counted. A seller with no
/// visible reviews has no rating, which is <c>null</c> rather than zero: zero is a score.
/// </remarks>
public sealed record SellerReportRowResponse(
    Guid SellerId,
    string StoreName,
    SellerStatus Status,
    int Products,
    int Orders,
    decimal GrossRevenue,
    decimal Commission,
    decimal NetEarnings,
    decimal? AverageRating,
    int ReviewCount);

public sealed record InventoryReportRowResponse(
    Guid ProductId,
    string ProductName,
    string Sku,
    string StoreName,
    int Available,
    int Reserved,
    int Sold,
    int Threshold,
    decimal StockValue);

/// <summary>
/// One seller in the commission report.
/// </summary>
/// <remarks>
/// Orders, gross, commission and seller earnings are scoped to the selected range. <b>Payouts</b>
/// and <b>PaidOut</b> count payouts <i>created</i> within the same range and only completed ones:
/// a payout record carries the window it was raised for, so "raised in this period" is the only
/// honest period question about it, and a pending payout is money that has not moved.
/// </remarks>
public sealed record CommissionReportRowResponse(
    Guid SellerId,
    string StoreName,
    int Orders,
    decimal GrossRevenue,
    decimal CommissionAmount,
    decimal SellerEarnings,
    int Payouts,
    decimal PaidOut);

/// <summary>One order, as the sales CSV writes it. Streamed rather than buffered.</summary>
public sealed record SalesExportRow(
    string OrderNumber,
    DateTimeOffset PlacedAt,
    string Status,
    decimal Subtotal,
    decimal Discount,
    decimal Shipping,
    decimal Tax,
    decimal Total,
    decimal Refunded,
    int ItemCount,
    int SellerCount);

public sealed record ActivityPointResponse(DateTimeOffset Period, int Count);

public sealed record PlatformHealthResponse(
    string Status,
    DateTimeOffset GeneratedAt,
    IReadOnlyDictionary<string, HealthIndicatorResponse> Components,
    IReadOnlyList<ActivityPointResponse>? Activity = null);

public sealed record HealthIndicatorResponse(string Status, string? Description, long DurationMs);
