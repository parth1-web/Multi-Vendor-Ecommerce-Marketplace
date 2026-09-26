using System.Globalization;
using System.Text;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Modules.Analytics.Abstractions;
using Marketplace.Application.Modules.Analytics.DTOs;
using Marketplace.Domain.Commissions;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Sellers;
using Microsoft.EntityFrameworkCore;
using InventoryRecord = Marketplace.Domain.Inventory.Inventory;
using OrderEntity = Marketplace.Domain.Orders.Order;

namespace Marketplace.Application.Modules.Analytics.Services;

/// <summary>Administrative reports and CSV export.</summary>
public sealed class ReportService(
    IRepository<OrderEntity> orders,
    IRepository<Seller> sellers,
    IRepository<SellerStore> stores,
    IRepository<Domain.Catalog.Product> products,
    IRepository<Commission> commissions,
    IRepository<SellerPayout> payouts,
    IRepository<InventoryRecord> inventories,
    IClock clock) : IReportService
{
    public async Task<IReadOnlyList<SalesReportRowResponse>> SalesAsync(DateTimeRange range, CancellationToken cancellationToken = default)
    {
        var rows = await orders.Query().AsNoTracking()
            .Where(o => o.PlacedAt >= range.From && o.PlacedAt <= range.To)
            .Select(o => new
            {
                o.PlacedAt,
                o.Status,
                o.Subtotal,
                o.DiscountAmount,
                o.ShippingAmount,
                o.TaxAmount,
                o.RefundedAmount,
                Commission = o.SellerOrders.Sum(so => so.CommissionAmount),
                Net = o.SellerOrders.Sum(so => so.SellerEarnings)
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return SellerAnalyticsService.Bucket(range, rows
                .Where(r => r.Status != OrderStatus.Cancelled)
                .Select(r => (r.PlacedAt, r.Subtotal, r.Commission, r.Net, 1)))
            .Select(p => new SalesReportRowResponse(
                p.Period,
                p.OrderCount,
                p.Revenue,
                0m,
                p.Commission,
                p.NetEarnings,
                0m,
                0m,
                0m,
                p.Revenue))
            .ToList();
    }

    public async Task<IReadOnlyList<SellerReportRowResponse>> SellersAsync(CancellationToken cancellationToken = default)
    {
        var sellerList = await sellers.Query().AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var storeList = await stores.Query().AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var storeBySeller = storeList.ToDictionary(s => s.SellerId, s => s.Name);

        var orderStats = await orders.Query().AsNoTracking()
            .SelectMany(o => o.SellerOrders)
            .GroupBy(so => so.SellerId)
            .Select(g => new
            {
                SellerId = g.Key,
                Orders = g.Count(),
                Revenue = g.Sum(so => so.Subtotal),
                Commission = g.Sum(so => so.CommissionAmount),
                Net = g.Sum(so => so.SellerEarnings)
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var productStats = await products.Query().AsNoTracking()
            .GroupBy(p => p.SellerId)
            .Select(g => new { SellerId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var orderMap = orderStats.ToDictionary(s => s.SellerId);
        var productMap = productStats.ToDictionary(p => p.SellerId, p => p.Count);

        return sellerList
            .Select(s =>
            {
                var stats = orderMap.GetValueOrDefault(s.Id);
                return new SellerReportRowResponse(
                    s.Id,
                    storeBySeller.GetValueOrDefault(s.Id, s.BusinessName),
                    s.Status,
                    productMap.GetValueOrDefault(s.Id),
                    stats?.Orders ?? 0,
                    decimal.Round(stats?.Revenue ?? 0m, 2),
                    decimal.Round(stats?.Commission ?? 0m, 2),
                    decimal.Round(stats?.Net ?? 0m, 2),
                    0m);
            })
            .OrderByDescending(r => r.GrossRevenue)
            .ToList();
    }

    public async Task<IReadOnlyList<InventoryReportRowResponse>> InventoryAsync(CancellationToken cancellationToken = default)
    {
        var rows = await inventories.Query().AsNoTracking()
            .OrderBy(i => i.AvailableQuantity)
            .Take(500)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
        var variantIds = rows.Select(r => r.ProductVariantId).ToList();
        var sellerIds = rows.Select(r => r.SellerId).Distinct().ToList();

        var productInfo = await products.Query().AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.BasePrice })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var variantSkus = await products.Query().AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .SelectMany(p => p.Variants)
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Sku })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var productMap = productInfo.ToDictionary(p => p.Id);
        var skuMap = variantSkus.ToDictionary(v => v.Id, v => v.Sku);

        var storeList = await stores.Query().AsNoTracking()
            .Where(s => sellerIds.Contains(s.SellerId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var storeMap = storeList.ToDictionary(s => s.SellerId, s => s.Name);

        return rows.Select(r =>
        {
            var product = productMap.GetValueOrDefault(r.ProductId);
            return new InventoryReportRowResponse(
                r.ProductId, product?.Name ?? "Unknown", skuMap.GetValueOrDefault(r.ProductVariantId, string.Empty),
                storeMap.GetValueOrDefault(r.SellerId, "Seller"),
                r.AvailableQuantity, r.ReservedQuantity, r.SoldQuantity, r.LowStockThreshold,
                decimal.Round(r.AvailableQuantity * (product?.BasePrice ?? 0m), 2));
        }).ToList();
    }

    public async Task<IReadOnlyList<CommissionReportRowResponse>> CommissionsAsync(DateTimeRange range, CancellationToken cancellationToken = default)
    {
        var rows = await commissions.Query().AsNoTracking()
            .Where(c => c.CreatedAt >= range.From && c.CreatedAt <= range.To)
            .GroupBy(c => c.SellerId)
            .Select(g => new
            {
                SellerId = g.Key,
                Orders = g.Count(),
                Gross = g.Sum(c => c.GrossAmount),
                Commission = g.Sum(c => c.CommissionAmount),
                Seller = g.Sum(c => c.SellerAmount)
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var sellerIds = rows.Select(r => r.SellerId).ToList();
        var storeList = await stores.Query().AsNoTracking()
            .Where(s => sellerIds.Contains(s.SellerId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var storeMap = storeList.ToDictionary(s => s.SellerId, s => s.Name);

        var payoutRows = await payouts.Query().AsNoTracking()
            .Where(p => sellerIds.Contains(p.SellerId) && p.Status == PayoutStatus.Completed)
            .GroupBy(p => p.SellerId)
            .Select(g => new { SellerId = g.Key, Payouts = g.Count(), Paid = g.Sum(p => p.NetAmount) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var payoutMap = payoutRows.ToDictionary(p => p.SellerId, p => (p.Payouts, p.Paid));

        return rows.Select(r =>
        {
            payoutMap.TryGetValue(r.SellerId, out var payout);
            return new CommissionReportRowResponse(
                r.SellerId,
                storeMap.GetValueOrDefault(r.SellerId, "Seller"),
                r.Orders,
                decimal.Round(r.Gross, 2),
                decimal.Round(r.Commission, 2),
                decimal.Round(r.Seller, 2),
                payout.Payouts,
                decimal.Round(payout.Paid, 2));
        })
            .OrderByDescending(r => r.CommissionAmount)
            .ToList();
    }

    public async Task<string> ExportSalesCsvAsync(DateTimeRange range, CancellationToken cancellationToken = default)
    {
        var rows = await orders.Query().AsNoTracking()
            .Where(o => o.PlacedAt >= range.From && o.PlacedAt <= range.To)
            .OrderByDescending(o => o.PlacedAt)
            .Select(o => new { o.OrderNumber, o.PlacedAt, o.Status, o.Subtotal, o.DiscountAmount, o.ShippingAmount, o.TaxAmount, o.TotalAmount, o.RefundedAmount, ItemCount = o.Items.Count, SellerCount = o.SellerOrders.Count })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var csv = new StringBuilder();
        csv.AppendLine("Order Number,Date,Status,Subtotal,Discount,Shipping,Tax,Total,Refunded,Items,Sellers");

        foreach (var row in rows)
        {
            csv.Append(row.OrderNumber).Append(',')
                .Append(row.PlacedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Status).Append(',')
                .Append(row.Subtotal.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.DiscountAmount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.ShippingAmount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.TaxAmount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.TotalAmount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.RefundedAmount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.ItemCount).Append(',')
                .Append(row.SellerCount)
                .AppendLine();
        }

        _ = clock;
        return csv.ToString();
    }
}
