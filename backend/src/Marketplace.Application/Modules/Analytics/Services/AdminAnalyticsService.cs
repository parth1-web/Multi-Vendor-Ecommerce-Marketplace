using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Modules.Analytics.Abstractions;
using Marketplace.Application.Modules.Analytics.DTOs;
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
using WishlistEntity = Marketplace.Domain.Cart.Wishlist;

namespace Marketplace.Application.Modules.Analytics.Services;

/// <summary>Platform-wide metrics for the admin dashboard.</summary>
public sealed class AdminAnalyticsService(
    IRepository<User> users,
    IRepository<Seller> sellers,
    IRepository<Domain.Catalog.Product> products,
    IRepository<OrderEntity> orders,
    IRepository<Commission> commissions,
    IRepository<Refund> refunds,
    IRepository<SellerPayout> payouts,
    IRepository<Domain.Orders.OrderItem> orderItems,
    IRepository<Category> categories,
    IClock clock) : IAdminAnalyticsService
{
    public async Task<AdminSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var today = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

        var allOrders = orders.Query().AsNoTracking().Where(o => o.Status != OrderStatus.Cancelled);

        var totalRevenue = await allOrders.SumAsync(o => (decimal?)o.TotalAmount, cancellationToken).ConfigureAwait(false) ?? 0m;
        var revenueToday = await allOrders.Where(o => o.PlacedAt >= today).SumAsync(o => (decimal?)o.TotalAmount, cancellationToken).ConfigureAwait(false) ?? 0m;
        var refunded = await refunds.Query().AsNoTracking()
            .Where(r => r.Status == RefundStatus.Completed)
            .SumAsync(r => (decimal?)r.Amount, cancellationToken).ConfigureAwait(false) ?? 0m;

        var totalOrders = await allOrders.CountAsync(cancellationToken).ConfigureAwait(false);
        var commissionRevenue = await commissions.Query().AsNoTracking()
            .Where(c => c.Status != CommissionStatus.Reversed)
            .SumAsync(c => (decimal?)c.CommissionAmount, cancellationToken).ConfigureAwait(false) ?? 0m;

        var sellerPayouts = await payouts.Query().AsNoTracking()
            .Where(p => p.Status == PayoutStatus.Completed)
            .SumAsync(p => (decimal?)p.NetAmount, cancellationToken).ConfigureAwait(false) ?? 0m;

        return new AdminSummaryResponse(
            await users.Query().AsNoTracking().CountAsync(u => u.Role == UserRole.Customer, cancellationToken).ConfigureAwait(false),
            await sellers.Query().AsNoTracking().CountAsync(cancellationToken).ConfigureAwait(false),
            await sellers.Query().AsNoTracking().CountAsync(s => s.Status == SellerStatus.Active, cancellationToken).ConfigureAwait(false),
            await sellers.Query().AsNoTracking().CountAsync(s => s.Status == SellerStatus.Pending, cancellationToken).ConfigureAwait(false),
            await sellers.Query().AsNoTracking().CountAsync(s => s.Status == SellerStatus.Suspended, cancellationToken).ConfigureAwait(false),
            await products.Query().AsNoTracking().CountAsync(p => !p.IsDeleted, cancellationToken).ConfigureAwait(false),
            await products.Query().AsNoTracking().CountAsync(p => p.Status == ProductStatus.Published && !p.IsDeleted, cancellationToken).ConfigureAwait(false),
            await products.Query().AsNoTracking().CountAsync(p => p.Status == ProductStatus.PendingApproval, cancellationToken).ConfigureAwait(false),
            totalOrders,
            await orders.Query().AsNoTracking().CountAsync(o => o.Status == OrderStatus.Pending, cancellationToken).ConfigureAwait(false),
            await refunds.Query().AsNoTracking().CountAsync(r => r.Status == RefundStatus.Requested || r.Status == RefundStatus.UnderReview, cancellationToken).ConfigureAwait(false),
            decimal.Round(totalRevenue, 2),
            decimal.Round(commissionRevenue, 2),
            decimal.Round(sellerPayouts, 2),
            decimal.Round(refunded, 2),
            totalRevenue <= 0m ? 0m : decimal.Round(refunded / totalRevenue * 100m, 2),
            totalOrders == 0 ? 0m : decimal.Round(totalRevenue / totalOrders, 2),
            0m,
            await users.Query().AsNoTracking().CountAsync(u => u.Role == UserRole.Customer && u.CreatedAt >= monthStart, cancellationToken).ConfigureAwait(false),
            await sellers.Query().AsNoTracking().CountAsync(s => s.AppliedAt >= monthStart, cancellationToken).ConfigureAwait(false),
            await orders.Query().AsNoTracking().CountAsync(o => o.PlacedAt >= today, cancellationToken).ConfigureAwait(false),
            decimal.Round(revenueToday, 2),
            now);
    }

    public async Task<IReadOnlyList<RevenuePointResponse>> GetRevenueAsync(DateTimeRange range, CancellationToken cancellationToken = default)
    {
        var rows = await orders.Query().AsNoTracking()
            .Where(o => o.PlacedAt >= range.From && o.PlacedAt <= range.To && o.Status != OrderStatus.Cancelled)
            .Select(o => new
            {
                o.PlacedAt,
                o.TotalAmount,
                Commission = o.SellerOrders.Sum(so => so.CommissionAmount),
                Net = o.SellerOrders.Sum(so => so.SellerEarnings),
                ItemCount = o.Items.Count
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return SellerAnalyticsService.Bucket(range,
            rows.Select(r => (r.PlacedAt, r.TotalAmount, r.Commission, r.Net, r.ItemCount)));
    }

    public async Task<IReadOnlyList<GrowthPointResponse>> GetGrowthAsync(DateTimeRange range, CancellationToken cancellationToken = default)
    {
        var customers = await users.Query().AsNoTracking()
            .Where(u => u.Role == UserRole.Customer && u.CreatedAt >= range.From && u.CreatedAt <= range.To)
            .Select(u => u.CreatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);

        var sellerList = await sellers.Query().AsNoTracking()
            .Where(s => s.AppliedAt >= range.From && s.AppliedAt <= range.To)
            .Select(s => s.AppliedAt).ToListAsync(cancellationToken).ConfigureAwait(false);

        var productList = await products.Query().AsNoTracking()
            .Where(p => p.CreatedAt >= range.From && p.CreatedAt <= range.To)
            .Select(p => p.CreatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);

        var orderList = await orders.Query().AsNoTracking()
            .Where(o => o.PlacedAt >= range.From && o.PlacedAt <= range.To)
            .Select(o => o.PlacedAt).ToListAsync(cancellationToken).ConfigureAwait(false);

        static DateTimeOffset Key(DateTimeOffset value, AnalyticsInterval interval) => interval switch
        {
            AnalyticsInterval.Month => new DateTimeOffset(value.Year, value.Month, 1, 0, 0, 0, TimeSpan.Zero),
            AnalyticsInterval.Year => new DateTimeOffset(value.Year, 1, 1, 0, 0, 0, TimeSpan.Zero),
            AnalyticsInterval.Week => value.Date.AddDays(-(int)value.DayOfWeek),
            _ => value.Date
        };

        var result = new Dictionary<DateTimeOffset, (int C, int S, int P, int O)>();
        Accumulate(customers, v => result[Key(v, range.Interval)] = Add(result.GetValueOrDefault(Key(v, range.Interval)), 1, 0, 0, 0));
        Accumulate(sellerList, v => result[Key(v, range.Interval)] = Add(result.GetValueOrDefault(Key(v, range.Interval)), 0, 1, 0, 0));
        Accumulate(productList, v => result[Key(v, range.Interval)] = Add(result.GetValueOrDefault(Key(v, range.Interval)), 0, 0, 1, 0));
        Accumulate(orderList, v => result[Key(v, range.Interval)] = Add(result.GetValueOrDefault(Key(v, range.Interval)), 0, 0, 0, 1));

        return result.OrderBy(kv => kv.Key)
            .Select(kv => new GrowthPointResponse(kv.Key, kv.Value.C, kv.Value.S, kv.Value.P, kv.Value.O))
            .ToList();

        static void Accumulate(List<DateTimeOffset> source, Action<DateTimeOffset> action)
        {
            foreach (var value in source)
            {
                action(value);
            }
        }

        static (int C, int S, int P, int O) Add((int C, int S, int P, int O) current, int c, int s, int p, int o) =>
            (current.C + c, current.S + s, current.P + p, current.O + o);
    }

    public async Task<IReadOnlyList<CategorySalesResponse>> GetCategoryPerformanceAsync(DateTimeRange range, CancellationToken cancellationToken = default)
    {
        var rows = await orderItems.Query().AsNoTracking()
            .Where(i => i.CreatedAt >= range.From && i.CreatedAt <= range.To)
            .GroupBy(i => i.CategoryId)
            .Select(g => new
            {
                CategoryId = g.Key,
                Quantity = g.Sum(i => i.Quantity),
                Revenue = g.Sum(i => i.LineTotal)
            })
            .OrderByDescending(x => x.Revenue)
            .Take(20)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var info = await categories.Query().AsNoTracking()
            .Where(c => rows.Select(r => r.CategoryId).Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => (c.Name, c.SlugValue), cancellationToken)
            .ConfigureAwait(false);

        var total = rows.Sum(r => r.Revenue);

        return rows.Select(r =>
        {
            var category = info.GetValueOrDefault(r.CategoryId);
            return new CategorySalesResponse(
                r.CategoryId, category.Name ?? "Uncategorised", category.SlugValue ?? string.Empty,
                r.Quantity, decimal.Round(r.Revenue, 2),
                total <= 0m ? 0m : decimal.Round(r.Revenue / total * 100m, 2));
        }).ToList();
    }

    public async Task<RefundAnalyticsResponse> GetRefundAnalyticsAsync(DateTimeRange range, CancellationToken cancellationToken = default)
    {
        var source = refunds.Query().AsNoTracking().Where(r => r.RequestedAt >= range.From && r.RequestedAt <= range.To);

        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        var approved = await source.CountAsync(r => r.Status == RefundStatus.Completed, cancellationToken).ConfigureAwait(false);
        var rejected = await source.CountAsync(r => r.Status == RefundStatus.Rejected, cancellationToken).ConfigureAwait(false);
        var pending = await source.CountAsync(r => r.Status == RefundStatus.Requested || r.Status == RefundStatus.UnderReview, cancellationToken).ConfigureAwait(false);
        var amount = await source.SumAsync(r => (decimal?)r.Amount, cancellationToken).ConfigureAwait(false) ?? 0m;

        var ordersInRange = await orders.Query().AsNoTracking()
            .CountAsync(o => o.PlacedAt >= range.From && o.PlacedAt <= range.To, cancellationToken)
            .ConfigureAwait(false);

        return new RefundAnalyticsResponse(
            total, approved, rejected, pending, decimal.Round(amount, 2),
            ordersInRange == 0 ? 0m : decimal.Round((decimal)total / ordersInRange * 100m, 2));
    }
}

/// <summary>Customer dashboard summary.</summary>
public sealed class CustomerAnalyticsService(
    IRepository<OrderEntity> orders,
    IRepository<WishlistEntity> wishlists,
    IRepository<UserAddress> addresses,
    IRepository<Notification> notifications,
    ICurrentUser currentUser,
    IClock clock) : ICustomerAnalyticsService
{
    public async Task<CustomerSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var mine = orders.Query().AsNoTracking().Where(o => o.CustomerId == currentUser.UserId);
        var paid = mine.Where(o => o.Status != OrderStatus.Cancelled);

        var totalOrders = await mine.CountAsync(cancellationToken).ConfigureAwait(false);
        var totalSpent = await paid.SumAsync(o => (decimal?)o.TotalAmount, cancellationToken).ConfigureAwait(false) ?? 0m;

        var recent = await mine
            .OrderByDescending(o => o.PlacedAt)
            .Take(5)
            .Select(o => new RecentOrderSummaryResponse(o.Id, o.OrderNumber, o.Status, o.TotalAmount, o.Currency, o.PlacedAt, o.Items.Count))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lastOrder = recent.Count > 0 ? recent[0].PlacedAt : (DateTimeOffset?)null;
        _ = clock;

        return new CustomerSummaryResponse(
            totalOrders,
            decimal.Round(totalSpent, 2),
            totalOrders == 0 ? 0m : decimal.Round(totalSpent / totalOrders, 2),
            await wishlists.Query().AsNoTracking().Where(w => w.UserId == currentUser.UserId).SelectMany(w => w.Items).CountAsync(cancellationToken).ConfigureAwait(false),
            await addresses.Query().AsNoTracking().CountAsync(a => a.UserId == currentUser.UserId, cancellationToken).ConfigureAwait(false),
            await notifications.Query().AsNoTracking().CountAsync(n => n.UserId == currentUser.UserId && !n.IsRead, cancellationToken).ConfigureAwait(false),
            await mine.CountAsync(o => o.Status == OrderStatus.Delivered, cancellationToken).ConfigureAwait(false),
            await mine.CountAsync(o => o.Status == OrderStatus.Pending, cancellationToken).ConfigureAwait(false),
            lastOrder,
            recent);
    }
}
