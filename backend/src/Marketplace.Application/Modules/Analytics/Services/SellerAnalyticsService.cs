using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Modules.Analytics.Abstractions;
using Marketplace.Application.Modules.Analytics.DTOs;
using Marketplace.Application.Modules.Orders.DTOs;
using Marketplace.Domain.Cart;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Commissions;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Identity;
using Marketplace.Domain.Notifications;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Refunds;
using Marketplace.Domain.Reviews;
using Marketplace.Domain.Sellers;
using Microsoft.EntityFrameworkCore;
using InventoryRecord = Marketplace.Domain.Inventory.Inventory;
using OrderEntity = Marketplace.Domain.Orders.Order;

namespace Marketplace.Application.Modules.Analytics.Services;

/// <summary>Seller dashboard metrics. Every query is scoped to the token's seller id.</summary>
public sealed class SellerAnalyticsService(
    IRepository<SellerOrder> sellerOrders,
    IRepository<OrderItem> orderItems,
    IRepository<Commission> commissions,
    IRepository<Domain.Catalog.Product> products,
    IRepository<Category> categories,
    IRepository<InventoryRecord> inventories,
    IRepository<Review> reviews,
    ICurrentUser currentUser,
    IClock clock) : ISellerAnalyticsService
{
    public async Task<SellerSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.SellerId is not { } sellerId)
        {
            return EmptySummary();
        }

        var now = clock.UtcNow;
        var today = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

        var orders = sellerOrders.Query().AsNoTracking().Where(so => so.SellerId == sellerId);
        var live = orders.Where(so => so.Status != SellerOrderStatus.Cancelled);

        var totalSales = await live.SumAsync(so => (decimal?)so.Subtotal, cancellationToken).ConfigureAwait(false) ?? 0m;
        var todaySales = await live.Where(so => so.CreatedAt >= today).SumAsync(so => (decimal?)so.Subtotal, cancellationToken).ConfigureAwait(false) ?? 0m;
        var monthSales = await live.Where(so => so.CreatedAt >= monthStart).SumAsync(so => (decimal?)so.Subtotal, cancellationToken).ConfigureAwait(false) ?? 0m;
        var totalOrders = await live.CountAsync(cancellationToken).ConfigureAwait(false);

        var sellerProducts = products.Query().AsNoTracking().Where(p => p.SellerId == sellerId && !p.IsDeleted);

        // Low stock is a property of the variant inventory rows, not the product itself.
        var sellerProductIds = products.Query().Where(p => p.SellerId == sellerId && !p.IsDeleted).Select(p => p.Id);
        var lowStock = await inventories.Query().AsNoTracking()
            .CountAsync(i => sellerProductIds.Contains(i.ProductId) &&
                           i.AvailableQuantity - i.ReservedQuantity <= i.LowStockThreshold &&
                           i.AvailableQuantity - i.ReservedQuantity > 0, cancellationToken)
            .ConfigureAwait(false);

        var outOfStock = await inventories.Query().AsNoTracking()
            .CountAsync(i => sellerProductIds.Contains(i.ProductId) && i.AvailableQuantity - i.ReservedQuantity <= 0, cancellationToken)
            .ConfigureAwait(false);

        var rating = await reviews.Query().AsNoTracking()
            .Where(r => r.SellerId == sellerId && r.IsVisible)
            .GroupBy(_ => 1)
            .Select(g => new { Average = g.Average(r => (decimal)r.Rating), Count = g.Count() })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var unanswered = await reviews.Query().AsNoTracking()
            .CountAsync(r => r.SellerId == sellerId && r.Reply == null, cancellationToken)
            .ConfigureAwait(false);

        var pendingEarnings = await commissions.Query().AsNoTracking()
            .Where(c => c.SellerId == sellerId && c.Status == CommissionStatus.Accrued)
            .SumAsync(c => (decimal?)c.SellerAmount, cancellationToken).ConfigureAwait(false) ?? 0m;

        var paidEarnings = await commissions.Query().AsNoTracking()
            .Where(c => c.SellerId == sellerId && c.Status == CommissionStatus.Paid)
            .SumAsync(c => (decimal?)c.SellerAmount, cancellationToken).ConfigureAwait(false) ?? 0m;

        var commissionPaid = await commissions.Query().AsNoTracking()
            .Where(c => c.SellerId == sellerId)
            .SumAsync(c => (decimal?)c.CommissionAmount, cancellationToken).ConfigureAwait(false) ?? 0m;

        var lastOrder = await live.OrderByDescending(so => so.CreatedAt)
            .Select(so => (DateTimeOffset?)so.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return new SellerSummaryResponse(
            decimal.Round(totalSales, 2),
            decimal.Round(todaySales, 2),
            decimal.Round(monthSales, 2),
            decimal.Round(pendingEarnings, 2),
            decimal.Round(paidEarnings, 2),
            decimal.Round(commissionPaid, 2),
            totalOrders,
            await orders.CountAsync(so => so.Status == SellerOrderStatus.Pending, cancellationToken).ConfigureAwait(false),
            await orders.CountAsync(so => so.Status == SellerOrderStatus.Processing, cancellationToken).ConfigureAwait(false),
            await orders.CountAsync(so => so.Status == SellerOrderStatus.Shipped, cancellationToken).ConfigureAwait(false),
            await orders.CountAsync(so => so.Status == SellerOrderStatus.Completed, cancellationToken).ConfigureAwait(false),
            await orders.CountAsync(so => so.Status == SellerOrderStatus.Cancelled, cancellationToken).ConfigureAwait(false),
            await sellerProducts.CountAsync(cancellationToken).ConfigureAwait(false),
            await sellerProducts.CountAsync(p => p.Status == ProductStatus.Published, cancellationToken).ConfigureAwait(false),
            await sellerProducts.CountAsync(p => p.Status == ProductStatus.PendingApproval, cancellationToken).ConfigureAwait(false),
            lowStock,
            outOfStock,
            rating?.Average is { } avg ? decimal.Round(avg, 2, MidpointRounding.AwayFromZero) : 0m,
            rating?.Count ?? 0,
            unanswered,
            totalOrders == 0 ? 0m : decimal.Round(totalSales / totalOrders, 2),
            lastOrder);
    }

    public async Task<IReadOnlyList<RevenuePointResponse>> GetRevenueAsync(DateTimeRange range, CancellationToken cancellationToken = default)
    {
        if (currentUser.SellerId is not { } sellerId)
        {
            return [];
        }

        var rows = await sellerOrders.Query().AsNoTracking()
            .Where(so => so.SellerId == sellerId && so.CreatedAt >= range.From && so.CreatedAt <= range.To && so.Status != SellerOrderStatus.Cancelled)
            .Select(so => new { so.CreatedAt, so.Subtotal, so.CommissionAmount, so.SellerEarnings, ItemCount = so.Items.Count })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Bucket(range, rows.Select(r => (r.CreatedAt, r.Subtotal, r.CommissionAmount, r.SellerEarnings, r.ItemCount)));
    }

    public async Task<IReadOnlyList<TopProductResponse>> GetTopProductsAsync(DateTimeRange range, int take, CancellationToken cancellationToken = default)
    {
        if (currentUser.SellerId is not { } sellerId)
        {
            return [];
        }

        var rows = await orderItems.Query().AsNoTracking()
            .Where(i => i.SellerId == sellerId && i.CreatedAt >= range.From && i.CreatedAt <= range.To)
            .GroupBy(i => new { i.ProductId, i.ProductName })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.ProductName,
                Quantity = g.Sum(i => i.Quantity),
                Revenue = g.Sum(i => i.LineTotal)
            })
            .OrderByDescending(x => x.Revenue)
            .Take(Math.Clamp(take, 1, 50))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var images = await products.Query().AsNoTracking()
            .Where(p => rows.Select(r => r.ProductId).Contains(p.Id))
            .Select(p => new { p.Id, Url = p.Images.Select(i => i.Url).FirstOrDefault() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var imageMap = images.ToDictionary(i => i.Id, i => i.Url);
        var commissions = await CommissionByProductAsync(
            sellerId, range, rows.Select(r => r.ProductId).ToList(), cancellationToken).ConfigureAwait(false);

        return rows.Select(r => new TopProductResponse(
            r.ProductId, r.ProductName, imageMap.GetValueOrDefault(r.ProductId),
            r.Quantity, decimal.Round(r.Revenue, 2),
            decimal.Round(CommissionFor(commissions, r.ProductId), 2))).ToList();
    }

    /// <summary>
    /// The commission booked against one product, in the window.
    /// </summary>
    /// <remarks>
    /// A commission is booked per seller order, not per line, so it is shared out in proportion
    /// to what each line contributed. Reporting zero, or the seller's current rate applied to
    /// revenue, would both be a number the finance ledger does not agree with.
    /// </remarks>
    private async Task<Dictionary<Guid, decimal>> CommissionByProductAsync(
        Guid sellerId, DateTimeRange range, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var orders = await commissions.Query().AsNoTracking()
            .Where(c => c.SellerId == sellerId)
            .Select(c => new { c.SellerOrderId, c.CommissionAmount })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (orders.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var orderIds = orders.Select(o => o.SellerOrderId).ToList();

        var lines = await orderItems.Query().AsNoTracking()
            .Where(i => orderIds.Contains(i.SellerOrderId) && productIds.Contains(i.ProductId))
            .Select(i => new { i.SellerOrderId, i.ProductId, Revenue = i.LineTotal })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var commissionByOrder = orders.ToDictionary(o => o.SellerOrderId, o => o.CommissionAmount);
        var result = new Dictionary<Guid, decimal>();

        foreach (var group in lines.GroupBy(l => l.SellerOrderId))
        {
            var orderRevenue = group.Sum(l => l.Revenue);
            if (orderRevenue <= 0m || !commissionByOrder.TryGetValue(group.Key, out var commission))
            {
                continue;
            }

            foreach (var line in group)
            {
                var share = decimal.Round(commission * line.Revenue / orderRevenue, 4);
                result[line.ProductId] = result.GetValueOrDefault(line.ProductId) + share;
            }
        }

        return result;
    }

    private static decimal CommissionFor(Dictionary<Guid, decimal> commissions, Guid productId) =>
        commissions.GetValueOrDefault(productId);

    public async Task<IReadOnlyList<CategorySalesResponse>> GetSalesByCategoryAsync(DateTimeRange range, CancellationToken cancellationToken = default)
    {
        if (currentUser.SellerId is not { } sellerId)
        {
            return [];
        }

        var rows = await orderItems.Query().AsNoTracking()
            .Where(i => i.SellerId == sellerId && i.CreatedAt >= range.From && i.CreatedAt <= range.To)
            .GroupBy(i => new { i.CategoryId })
            .Select(g => new
            {
                g.Key.CategoryId,
                Quantity = g.Sum(i => i.Quantity),
                Revenue = g.Sum(i => i.LineTotal)
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var categoryIds = rows.Select(r => r.CategoryId).ToList();
        var categoryInfo = await categories.Query().AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => (c.Name, c.SlugValue), cancellationToken)
            .ConfigureAwait(false);

        var total = rows.Sum(r => r.Revenue);

        return rows
            .OrderByDescending(r => r.Revenue)
            .Select(r =>
            {
                var info = categoryInfo.GetValueOrDefault(r.CategoryId);
                return new CategorySalesResponse(
                    r.CategoryId, info.Name ?? "Uncategorised", info.SlugValue ?? string.Empty,
                    r.Quantity, decimal.Round(r.Revenue, 2),
                    total <= 0m ? 0m : decimal.Round(r.Revenue / total * 100m, 2));
            })
            .ToList();
    }

    public async Task<IReadOnlyList<OrderStatusCountResponse>> GetOrderStatusBreakdownAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.SellerId is not { } sellerId)
        {
            return [];
        }

        var rows = await sellerOrders.Query().AsNoTracking()
            .Where(so => so.SellerId == sellerId)
            .GroupBy(so => so.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Total = g.Sum(so => so.TotalAmount) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(r => new OrderStatusCountResponse((OrderStatus)r.Status, r.Count, decimal.Round(r.Total, 2))).ToList();
    }

    private static SellerSummaryResponse EmptySummary() =>
        new(0m, 0m, 0m, 0m, 0m, 0m, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0m, 0, 0, 0m, null);
    internal static IReadOnlyList<RevenuePointResponse> Bucket(
        DateTimeRange range,
        IEnumerable<(DateTimeOffset At, decimal Revenue, decimal Commission, decimal Net, int Items)> rows)
    {
        var buckets = new Dictionary<DateTimeOffset, (decimal Revenue, decimal Commission, decimal Net, int Orders, int Items)>();

        foreach (var row in rows)
        {
            var key = range.Interval switch
            {
                AnalyticsInterval.Month => new DateTimeOffset(row.At.Year, row.At.Month, 1, 0, 0, 0, TimeSpan.Zero),
                AnalyticsInterval.Year => new DateTimeOffset(row.At.Year, 1, 1, 0, 0, 0, TimeSpan.Zero),
                AnalyticsInterval.Week => row.At.Date.AddDays(-(int)row.At.DayOfWeek),
                _ => row.At.Date
            };

            var current = buckets.GetValueOrDefault(key);
            buckets[key] = (current.Revenue + row.Revenue, current.Commission + row.Commission, current.Net + row.Net, current.Orders + 1, current.Items + row.Items);
        }

        var ordered = buckets.OrderBy(kv => kv.Key).ToList();

        // Fill gaps so charts never show a misleading straight line.
        var result = new List<RevenuePointResponse>();
        for (var cursor = range.From; cursor <= range.To;)
        {
            var key = range.Interval switch
            {
                AnalyticsInterval.Month => new DateTimeOffset(cursor.Year, cursor.Month, 1, 0, 0, 0, TimeSpan.Zero),
                AnalyticsInterval.Year => new DateTimeOffset(cursor.Year, 1, 1, 0, 0, 0, TimeSpan.Zero),
                AnalyticsInterval.Week => cursor.Date.AddDays(-(int)cursor.DayOfWeek),
                _ => cursor.Date
            };

            if (buckets.TryGetValue(key, out var value))
            {
                result.Add(new RevenuePointResponse(key,
                    decimal.Round(value.Revenue, 2), decimal.Round(value.Commission, 2),
                    decimal.Round(value.Net, 2), value.Orders, value.Items));
            }
            else
            {
                result.Add(new RevenuePointResponse(key, 0m, 0m, 0m, 0, 0));
            }

            cursor = range.Interval switch
            {
                AnalyticsInterval.Month => cursor.AddMonths(1),
                AnalyticsInterval.Year => cursor.AddYears(1),
                AnalyticsInterval.Week => cursor.AddDays(7),
                _ => cursor.AddDays(1)
            };
        }

        return result;
    }
}
