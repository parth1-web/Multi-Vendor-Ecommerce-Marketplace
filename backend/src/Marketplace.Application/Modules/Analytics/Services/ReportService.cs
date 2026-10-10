using System.Runtime.CompilerServices;
using Marketplace.Application.Common;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Analytics.Abstractions;
using Marketplace.Application.Modules.Analytics.DTOs;
using Marketplace.Domain.Commissions;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Reviews;
using Marketplace.Domain.Sellers;
using Microsoft.EntityFrameworkCore;
using InventoryRecord = Marketplace.Domain.Inventory.Inventory;
using OrderEntity = Marketplace.Domain.Orders.Order;

namespace Marketplace.Application.Modules.Analytics.Services;

/// <summary>
/// Administrative reports and the sales CSV.
/// </summary>
/// <remarks>
/// The rule this class is written to: a report aggregates what the database already persisted.
/// Discount, tax, shipping and refunds are read from the order that recorded them at checkout, so
/// a report of March is still correct after next month's tax rate changes or a coupon is edited.
/// Nothing here recomputes a historical figure from current configuration, and nothing here sums a
/// whole table to paginate it — every list is filtered, counted and sliced by the database.
///
/// <para><b>There is no profit or margin report, and there cannot honestly be one yet.</b></para>
///
/// Every figure available here is a revenue figure. The system persists what a customer paid
/// (<c>Order.Subtotal</c>, <c>DiscountAmount</c>, <c>ShippingAmount</c>, <c>TaxAmount</c>,
/// <c>TotalAmount</c>, <c>RefundedAmount</c>), what the marketplace kept
/// (<c>SellerOrder.CommissionAmount</c>) and what the seller earned (<c>SellerOrder.SellerEarnings</c>).
/// It persists **no cost of goods anywhere**: <c>Product</c> has a selling <c>BasePrice</c> and a
/// <c>CompareAtPrice</c>, <c>ProductVariant</c> has a selling <c>Price</c>, and <c>OrderItem</c>
/// snapshots <c>UnitPrice</c> and <c>LineTotal</c> — all revenue-side, none of them what the seller
/// paid for the unit. No cost column exists on any entity, and none exists in any migration.
///
/// Gross profit is revenue minus cost, so with no cost recorded there is no gross profit, no margin
/// and no contribution figure to report. Subtracting today's product price from a historical order
/// would be inventing cost data: a seller's cost changes when they renegotiate with a supplier, and
/// an order placed last March was costed at last March's price, which no longer exists anywhere.
///
/// Adding it safely is possible but is a larger change than a report: a nullable cost on
/// <c>ProductVariant</c>, captured onto <c>OrderItem</c> at checkout, populated by seller input that
/// does not exist yet, and a report that reports coverage honestly because every order placed before
/// that change has no recorded cost. That belongs in its own phase with the product authoring work it
/// depends on.
/// </remarks>
public sealed class ReportService(
    IRepository<OrderEntity> orders,
    IRepository<Seller> sellers,
    IRepository<SellerStore> stores,
    IRepository<Domain.Catalog.Product> products,
    IRepository<Domain.Catalog.ProductVariant> variants,
    IRepository<Commission> commissions,
    IRepository<SellerPayout> payouts,
    IRepository<InventoryRecord> inventories,
    IRepository<Review> reviews) : IReportService
{
    /// <summary>
    /// Sales per period.
    /// </summary>
    /// <remarks>
    /// One row per bucket over the window, with empty buckets present as zero. A cancelled order
    /// is left out entirely: the shopper never paid for it, so counting its value as revenue
    /// would overstate the period. Every other status counts, because an order that has been
    /// placed is revenue the marketplace has earned the right to collect, whether it has shipped
    /// yet or not. Refunds are reported as their own column rather than by removing revenue, so a
    /// reader can see gross and returned separately.
    /// </remarks>
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

        var live = rows.Where(r => r.Status != OrderStatus.Cancelled).ToList();

        // The bucket boundaries are a function of the interval the API resolved, and the whole set
        // of them is known up front, so the rows are grouped by the same key the revenue chart uses
        // and every column is summed from the orders in that bucket. One small projection per order
        // inside the window — never the whole table.
        var buckets = live
            .GroupBy(r => BucketKey(range.Interval, r.PlacedAt))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var gross = Round(g.Sum(r => r.Subtotal));
                var discounts = Round(g.Sum(r => r.DiscountAmount));
                var refunds = Round(g.Sum(r => r.RefundedAmount));

                return new SalesReportRowResponse(
                    g.Key,
                    g.Count(),
                    gross,
                    discounts,
                    Round(g.Sum(r => r.Commission)),
                    Round(g.Sum(r => r.Net)),
                    Round(g.Sum(r => r.TaxAmount)),
                    Round(g.Sum(r => r.ShippingAmount)),
                    refunds,
                    // Net revenue: goods retained. Shipping and tax stay out — see the remarks on
                    // SalesReportRowResponse for why that is a decision rather than an omission.
                    Round(gross - discounts - refunds));
            })
            .ToList();

        // Empty buckets are filled in by the shared bucketing so a quiet week is a zero row rather
        // than a gap, and the periods line up with the revenue endpoint's chart.
        var periods = SellerAnalyticsService.Bucket(range,
            live.Select(r => (r.PlacedAt, 0m, 0m, 0m, 0)))
            .Select(p => p.Period)
            .ToHashSet();

        foreach (var period in periods.Where(period => buckets.All(b => b.Period != period)))
        {
            buckets.Insert(0, new SalesReportRowResponse(period, 0, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m));
        }

        return buckets.OrderBy(b => b.Period).ToList();
    }

    /// <summary>
    /// The bucket a timestamp belongs to, matching the interval the revenue analytics groups by.
    /// </summary>
    private static DateTimeOffset BucketKey(AnalyticsInterval interval, DateTimeOffset value) => interval switch
    {
        AnalyticsInterval.Month => new DateTimeOffset(value.Year, value.Month, 1, 0, 0, 0, TimeSpan.Zero),
        AnalyticsInterval.Year => new DateTimeOffset(value.Year, 1, 1, 0, 0, 0, TimeSpan.Zero),
        AnalyticsInterval.Week => value.Date.AddDays(-(int)value.DayOfWeek),
        _ => value.Date
    };

    /// <summary>
    /// Sellers, ranked by gross revenue.
    /// </summary>
    /// <remarks>
    /// The order, product and rating aggregates are each computed by one grouped query over the
    /// filtered set of sellers, so nothing walks the order table in memory. The sellers themselves
    /// are a small dimension table that has to be present to be sorted by an aggregate it does not
    /// own; paging happens after that sort, over rows that are already one per store.
    ///
    /// With no period supplied the aggregates are lifetime totals — what this report always meant.
    /// With one they cover the window, which is why the period is optional and its absence is not
    /// quietly the same thing as a wide window.
    ///
    /// Ratings come from the seller's visible reviews, which is the same set the storefront shows.
    /// </remarks>
    public async Task<PagedResult<SellerReportRowResponse>> SellersAsync(SellerReportQuery query, CancellationToken cancellationToken = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var range = query.Range;

        var source = sellers.Query().AsNoTracking().AsQueryable();

        if (query.Status is { } status)
        {
            source = source.Where(s => s.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = SearchPattern.Contains(query.Search);
            var matchingStores = stores.Query().AsNoTracking()
                .Where(st => EF.Functions.Like(st.Name.ToLower(), term))
                .Select(st => st.SellerId);

            source = source.Where(s => EF.Functions.Like(s.BusinessName.ToLower(), term) || matchingStores.Contains(s.Id));
        }

        var sellerIds = source.Select(s => s.Id);

        // One grouped query for the order aggregates, restricted to the filtered sellers and the
        // window if one was asked for.
        var orderStats = orders.Query().AsNoTracking()
            .SelectMany(o => o.SellerOrders)
            .Where(so => sellerIds.Contains(so.SellerId))
            .Where(so => range == null || (so.Order!.PlacedAt >= range.From && so.Order.PlacedAt <= range.To))
            .GroupBy(so => so.SellerId)
            .Select(g => new
            {
                SellerId = g.Key,
                Orders = g.Count(),
                Revenue = g.Sum(so => so.Subtotal),
                Commission = g.Sum(so => so.CommissionAmount),
                Net = g.Sum(so => so.SellerEarnings)
            })
            .ToDictionary(x => x.SellerId);

        var productStats = products.Query().AsNoTracking()
            .Where(p => sellerIds.Contains(p.SellerId))
            .GroupBy(p => p.SellerId)
            .Select(g => new { SellerId = g.Key, Count = g.Count() })
            .ToDictionary(x => x.SellerId, x => x.Count);

        // Visible reviews only, because that is what a shopper can see and therefore what a store
        // can fairly be judged on. A seller with none gets no rating at all.
        var ratingStats = reviews.Query().AsNoTracking()
            .Where(r => sellerIds.Contains(r.SellerId) && r.IsVisible)
            .GroupBy(r => r.SellerId)
            .Select(g => new
            {
                SellerId = g.Key,
                Count = g.Count(),
                Average = g.Average(r => (decimal?)r.Rating)
            })
            .ToDictionary(x => x.SellerId);

        var sellerList = await source.ToListAsync(cancellationToken).ConfigureAwait(false);
        var storeList = await stores.Query().AsNoTracking()
            .Where(st => sellerIds.Contains(st.SellerId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var storeBySeller = storeList.ToDictionary(s => s.SellerId, s => s.Name);

        var rows = sellerList
            .Select(seller =>
            {
                orderStats.TryGetValue(seller.Id, out var stats);
                productStats.TryGetValue(seller.Id, out var productCount);
                ratingStats.TryGetValue(seller.Id, out var rating);

                return new SellerReportRowResponse(
                    seller.Id,
                    storeBySeller.GetValueOrDefault(seller.Id, seller.BusinessName),
                    seller.Status,
                    productCount,
                    stats?.Orders ?? 0,
                    Round(stats?.Revenue ?? 0m),
                    Round(stats?.Commission ?? 0m),
                    Round(stats?.Net ?? 0m),
                    rating is null ? null : Round(rating.Average ?? 0m),
                    rating?.Count ?? 0);
            })
            .OrderByDescending(r => r.GrossRevenue)
            .ThenBy(r => r.StoreName, StringComparer.Ordinal)
            .ToList();

        var total = rows.Count;

        return new PagedResult<SellerReportRowResponse>(
            rows.Skip(page.Skip).Take(page.PageSize).ToList(),
            page.Page,
            page.PageSize,
            total);
    }

    /// <summary>
    /// Stock per variant, lowest available first, paged by the database.
    /// </summary>
    /// <remarks>
    /// The 500-row ceiling this report used to have is gone: it silently truncated the platform
    /// while looking like a complete answer. Now the filter runs first and the database counts and
    /// slices, so a total is honest and a page costs the same whatever the catalogue size.
    ///
    /// The order is available ascending with the variant id as a tie-break, because two variants
    /// can hold the same quantity and a page boundary that reshuffles between requests would show
    /// the same row twice.
    /// </remarks>
    public async Task<PagedResult<InventoryReportRowResponse>> InventoryAsync(InventoryReportQuery query, CancellationToken cancellationToken = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var source = inventories.Query().AsNoTracking();

        if (query.SellerId is { } sellerId)
        {
            source = source.Where(i => i.SellerId == sellerId);
        }

        if (query.LowStockOnly)
        {
            // Sellable, not available: stock held for an order is not stock a seller can sell, and
            // the seller inventory screen has always counted it this way.
            source = source.Where(i => i.AvailableQuantity - i.ReservedQuantity <= i.LowStockThreshold);
        }

        if (query.OutOfStockOnly)
        {
            source = source.Where(i => i.AvailableQuantity <= 0);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = SearchPattern.Contains(query.Search);
            var matchingProducts = products.Query().AsNoTracking()
                .Where(p => EF.Functions.Like(p.Name.ToLower(), term))
                .Select(p => p.Id);
            var matchingSkus = variants.Query().AsNoTracking()
                .Where(v => EF.Functions.Like(v.Sku.ToLower(), term))
                .Select(v => v.Id);
            var matchingStores = stores.Query().AsNoTracking()
                .Where(st => EF.Functions.Like(st.Name.ToLower(), term))
                .Select(st => st.SellerId);

            source = source.Where(i =>
                matchingProducts.Contains(i.ProductId)
                || matchingSkus.Contains(i.ProductVariantId)
                || matchingStores.Contains(i.SellerId));
        }

        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);

        // Only the page's rows are pulled, then hydrated with three lookups keyed on that page.
        var pageRows = await source
            .OrderBy(i => i.AvailableQuantity)
            .ThenBy(i => i.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hydrated = await HydrateInventoryAsync(pageRows, cancellationToken).ConfigureAwait(false);

        return new PagedResult<InventoryReportRowResponse>(hydrated, page.Page, page.PageSize, total);
    }

/// <summary>
    /// One seller's stock, for the seller themselves.
    /// </summary>
    /// <remarks>
    /// Delegates to the same implementation the admin report uses and then pins the seller, so the
    /// filters, the ordering and the total cannot drift between the two screens. The seller id is
    /// supplied by the caller of *this method* from the authenticated principal; the controller
    /// never reads it from a request.
    /// </remarks>
    public Task<PagedResult<InventoryReportRowResponse>> SellerInventoryAsync(
        Guid sellerId,
        SellerInventoryReportQuery query,
        CancellationToken cancellationToken = default) =>
        InventoryAsync(
            new InventoryReportQuery(query.Page, query.PageSize, query.Search, query.LowStockOnly, query.OutOfStockOnly, sellerId),
            cancellationToken);

    /// <summary>
    /// Commission earned per seller in the window, ranked by amount.
    /// </summary>
    /// <remarks>
    /// Payout columns are scoped by the period the payout <i>covers</i>, not by when the row was
    /// written. A payout records the window of commission accrual it settles — the job selects
    /// commissions where <c>CreatedAt &gt;= periodStart &amp;&amp; CreatedAt &lt; periodEnd</c> — and it
    /// is created afterwards, when the job next runs. Filtering on the row's own creation date
    /// therefore answers "when did we pay", which is not a reporting question.
    ///
    /// A payout whose period straddles a reporting boundary is assigned to the window containing
    /// its <c>PeriodEnd</c>. That counts every payout exactly once — an overlap rule would count a
    /// February payout in both January and February, and full containment would hide it from both —
    /// and it matches how the payout is described: it settles up to the moment its period ends.
    ///
    /// Only completed payouts count. A pending, processing, failed or cancelled payout is not money
    /// that has moved, and the commission columns above still show what was earned for the period
    /// regardless of whether it has been paid out yet.
    /// </remarks>
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
        var storeMap = await stores.Query().AsNoTracking()
            .Where(s => sellerIds.Contains(s.SellerId))
            .ToDictionaryAsync(s => s.SellerId, s => s.Name, cancellationToken)
            .ConfigureAwait(false);

        var payoutRows = await payouts.Query().AsNoTracking()
            .Where(p => sellerIds.Contains(p.SellerId)
                && p.Status == PayoutStatus.Completed
                && p.PeriodEnd > range.From
                && p.PeriodEnd <= range.To)
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
                Round(r.Gross),
                Round(r.Commission),
                Round(r.Seller),
                payout.Payouts,
                Round(payout.Paid));
        })
            .OrderByDescending(r => r.CommissionAmount)
            .ThenBy(r => r.StoreName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The orders an export covers, one page at a time, straight out of the database.
    /// </summary>
    /// <remarks>
    /// Cancelled orders are included on purpose: a CSV is a record of what happened, and an order
    /// that was placed and then cancelled happened. The sales report excludes them because it
    /// measures revenue; the export answers a different question and says so in its header row.
    /// </remarks>
    public async IAsyncEnumerable<SalesExportRow> StreamSalesAsync(
        DateTimeRange range,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var written = 0;

        await foreach (var row in orders.Query().AsNoTracking()
            .Where(o => o.PlacedAt >= range.From && o.PlacedAt <= range.To)
            .OrderByDescending(o => o.PlacedAt)
            .Select(o => new SalesExportRow(
                o.OrderNumber,
                o.PlacedAt,
                o.Status.ToString(),
                o.Subtotal,
                o.DiscountAmount,
                o.ShippingAmount,
                o.TaxAmount,
                o.TotalAmount,
                o.RefundedAmount,
                o.Items.Count,
                o.SellerOrders.Count))
            .AsAsyncEnumerable()
            .ConfigureAwait(false))
        {
            if (written >= IReportService.SalesExportRowLimit)
            {
                yield break;
            }

            written++;
            yield return row;
        }
    }

    /// <summary>Resolves one page of stock rows into names, SKUs and a store, for that page only.</summary>
    private async Task<List<InventoryReportRowResponse>> HydrateInventoryAsync(
        List<InventoryRecord> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
        var variantIds = rows.Select(r => r.ProductVariantId).ToList();
        var sellerIds = rows.Select(r => r.SellerId).Distinct().ToList();

        var productMap = await products.Query().AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.BasePrice })
            .ToDictionaryAsync(p => p.Id, cancellationToken)
            .ConfigureAwait(false);

        var skuMap = await variants.Query().AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Sku })
            .ToDictionaryAsync(v => v.Id, v => v.Sku, cancellationToken)
            .ConfigureAwait(false);

        var storeMap = await stores.Query().AsNoTracking()
            .Where(s => sellerIds.Contains(s.SellerId))
            .ToDictionaryAsync(s => s.SellerId, s => s.Name, cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(r =>
        {
            var product = productMap.GetValueOrDefault(r.ProductId);
            return new InventoryReportRowResponse(
                r.ProductId,
                // A variant whose product was deleted keeps its stock row. Saying so is better than
                // an empty cell, and better than dropping a row of stock out of the report.
                product?.Name ?? "Unknown product",
                skuMap.GetValueOrDefault(r.ProductVariantId, string.Empty),
                storeMap.GetValueOrDefault(r.SellerId, "Seller"),
                r.AvailableQuantity,
                r.ReservedQuantity,
                r.SoldQuantity,
                r.LowStockThreshold,
                Round(r.AvailableQuantity * (product?.BasePrice ?? 0m)));
        }).ToList();
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}