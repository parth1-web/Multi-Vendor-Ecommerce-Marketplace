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
    decimal ConversionRate,
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
    decimal ConversionRate,
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

public sealed record SellerReportRowResponse(
    Guid SellerId,
    string StoreName,
    SellerStatus Status,
    int Products,
    int Orders,
    decimal GrossRevenue,
    decimal Commission,
    decimal NetEarnings,
    decimal AverageRating);

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

public sealed record CommissionReportRowResponse(
    Guid SellerId,
    string StoreName,
    int Orders,
    decimal GrossRevenue,
    decimal CommissionAmount,
    decimal SellerEarnings,
    int Payouts,
    decimal PaidOut);

public sealed record ActivityPointResponse(DateTimeOffset Period, int Count);

public sealed record PlatformHealthResponse(
    string Status,
    DateTimeOffset GeneratedAt,
    IReadOnlyDictionary<string, HealthIndicatorResponse> Components,
    IReadOnlyList<ActivityPointResponse>? Activity = null);

public sealed record HealthIndicatorResponse(string Status, string? Description, long DurationMs);
