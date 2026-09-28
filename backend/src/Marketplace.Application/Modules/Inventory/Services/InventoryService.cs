using Marketplace.Application.Common.Extensions;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Auth.Abstractions;
using Marketplace.Application.Modules.Inventory.Abstractions;
using Marketplace.Application.Modules.Notifications.DTOs;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Events;
using Marketplace.Domain.Inventory;
using InventoryRecord = Marketplace.Domain.Inventory.Inventory;
using Marketplace.Domain.Sellers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marketplace.Application.Modules.Inventory.Services;

/// <summary>
/// Inventory management with concurrency-safe reservations.
///
/// Reservation is the critical path: a single conditional
/// <c>UPDATE … WHERE available - reserved &gt;= @qty</c> acts as the lock, so a
/// read-then-write race is impossible and the database itself prevents overselling.
/// </summary>
public sealed class InventoryService(
    IRepository<InventoryRecord> inventories,
    IRepository<InventoryTransaction> transactions,
    IRepository<InventoryReservation> reservations,
    IRepository<Product> products,
    IRepository<ProductVariant> variants,
    IRepository<Seller> sellers,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    IClock clock,
    IAuditService auditService,
    INotificationService notificationService,
    IRealtimeNotifier realtime,
    IOptions<MarketplaceOptions> options,
    ILogger<InventoryService> logger) : IInventoryService
{
    private readonly MarketplaceOptions _options = options.Value;

    public async Task<PagedResult<InventoryItemResponse>> ListAsync(InventoryListQuery query, CancellationToken cancellationToken = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var source = ScopedQuery().AsNoTracking();

        if (query.ProductId is { } productId)
        {
            source = source.Where(i => i.ProductId == productId);
        }

        if (query.LowStockOnly == true)
        {
            source = source.Where(i => i.AvailableQuantity - i.ReservedQuantity <= i.LowStockThreshold);
        }

        if (query.OutOfStockOnly == true)
        {
            source = source.Where(i => i.AvailableQuantity - i.ReservedQuantity <= 0);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            var productIds = products.Query().Where(p => EF.Functions.Like(p.Name, term)).Select(p => p.Id);
            var skus = variants.Query().Where(v => EF.Functions.Like(v.Sku, term)).Select(v => v.Id);
            source = source.Where(i => productIds.Contains(i.ProductId) || skus.Contains(i.ProductVariantId));
        }

        var result = await source
            .OrderBy(i => i.AvailableQuantity - i.ReservedQuantity)
            .ThenBy(i => i.Id)
            .ToPagedResultAsync(page, i => new InventoryItemResponse(
                i.Id, i.ProductId, i.ProductVariantId, string.Empty, null, string.Empty, string.Empty,
                i.AvailableQuantity, i.ReservedQuantity, i.SoldQuantity,
                i.AvailableQuantity - i.ReservedQuantity, i.LowStockThreshold,
                (i.AvailableQuantity - i.ReservedQuantity) <= i.LowStockThreshold,
                (i.AvailableQuantity - i.ReservedQuantity) <= 0, i.UpdatedAt), cancellationToken)
            .ConfigureAwait(false);

        // The hydrated items are the ones to return. The projection fills names in afterwards,
        // and returning the un-hydrated page instead is how a stock list ends up showing a
        // column of blank names and a default date.
        var hydrated = await HydrateAsync(result.Items.ToList(), cancellationToken).ConfigureAwait(false);

        return new PagedResult<InventoryItemResponse>(hydrated, result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<IReadOnlyList<InventoryItemResponse>> GetLowStockAsync(CancellationToken cancellationToken = default)
    {
        var items = await ScopedQuery().AsNoTracking()
            .Where(i => i.AvailableQuantity - i.ReservedQuantity <= i.LowStockThreshold)
            .OrderBy(i => i.AvailableQuantity - i.ReservedQuantity)
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await HydrateAsync(items.Select(i => new InventoryItemResponse(
            i.Id, i.ProductId, i.ProductVariantId, string.Empty, null, string.Empty, string.Empty,
            i.AvailableQuantity, i.ReservedQuantity, i.SoldQuantity,
            i.AvailableQuantity - i.ReservedQuantity, i.LowStockThreshold,
            (i.AvailableQuantity - i.ReservedQuantity) <= i.LowStockThreshold,
            (i.AvailableQuantity - i.ReservedQuantity) <= 0, i.UpdatedAt)).ToList(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<InventoryItemResponse>> AdjustAsync(Guid variantId, AdjustStockRequest request, CancellationToken cancellationToken = default)
    {
        if (currentUser.SellerId is null && !currentUser.IsAdmin)
        {
            return Result<InventoryItemResponse>.Failure("Only sellers can adjust stock.");
        }

        var inventory = await ScopedQuery().FirstOrDefaultAsync(i => i.ProductVariantId == variantId, cancellationToken).ConfigureAwait(false);
        if (inventory is null)
        {
            return Result<InventoryItemResponse>.Failure("Inventory record not found.", ResultErrorCodes.NotFound);
        }

        if (request.Delta < 0 && Math.Abs(request.Delta) > inventory.AvailableQuantity)
        {
            return Result<InventoryItemResponse>.Failure("Cannot remove more units than are physically on hand.");
        }

        var now = clock.UtcNow;
        var before = inventory.AvailableQuantity;
        var type = request.Delta > 0 ? InventoryTransactionType.Restock : InventoryTransactionType.Damage;

        // The ledger brackets what is on hand, so the row is written before the change.
        var transaction = InventoryTransaction.Record(inventory, type, request.Delta, before, "ManualAdjustment", null, request.Reason, currentUser.UserId, now);
        inventory.Adjust(request.Delta, now);
        await transactions.AddAsync(transaction, cancellationToken).ConfigureAwait(false);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.InventoryAdjusted, nameof(Inventory), inventory.Id, request.Reason,
            new { Before = before, After = inventory.AvailableQuantity, Delta = request.Delta }, cancellationToken).ConfigureAwait(false);

        await RaiseLowStockAsync(inventory, now, cancellationToken).ConfigureAwait(false);

        var dto = new InventoryItemResponse(
            inventory.Id, inventory.ProductId, inventory.ProductVariantId, string.Empty, null, string.Empty, string.Empty,
            inventory.AvailableQuantity, inventory.ReservedQuantity, inventory.SoldQuantity,
            inventory.SellableQuantity, inventory.LowStockThreshold, inventory.IsLowStock, inventory.IsOutOfStock, inventory.UpdatedAt);

        var hydrated = (await HydrateAsync([dto], cancellationToken).ConfigureAwait(false))[0];
        return Result<InventoryItemResponse>.Success(hydrated);
    }

    public async Task<Result> SetThresholdAsync(Guid variantId, SetThresholdRequest request, CancellationToken cancellationToken = default)
    {
        var inventory = await ScopedQuery().FirstOrDefaultAsync(i => i.ProductVariantId == variantId, cancellationToken).ConfigureAwait(false);
        if (inventory is null)
        {
            return Result.Failure("Inventory record not found.", ResultErrorCodes.NotFound);
        }

        inventory.SetLowStockThreshold(request.LowStockThreshold, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<IReadOnlyList<InventoryTransactionResponse>> GetTransactionsAsync(Guid variantId, int take, CancellationToken cancellationToken = default)
    {
        var sellerId = currentUser.SellerId;
        if (sellerId is null && !currentUser.IsAdmin)
        {
            return [];
        }

        return await transactions.Query().AsNoTracking()
            .Where(t => t.ProductVariantId == variantId && (currentUser.IsAdmin || t.SellerId == sellerId))
            .OrderByDescending(t => t.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .Select(t => new InventoryTransactionResponse(
                t.Id, t.Type, t.QuantityDelta, t.QuantityBefore, t.QuantityAfter,
                t.ReferenceType, t.ReferenceId, t.Reason, t.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<InventoryReservationOutcome>> ReserveAsync(Guid variantId, int quantity, Guid orderId, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
        {
            return Result<InventoryReservationOutcome>.Failure(new InventoryReservationOutcome(
                false, Guid.Empty, 0, 0, 0, string.Empty, string.Empty, string.Empty, "Quantity must be greater than zero."));
        }

        var inventory = await inventories.Query().AsNoTracking()
            .FirstOrDefaultAsync(i => i.ProductVariantId == variantId, cancellationToken)
            .ConfigureAwait(false);

        if (inventory is null)
        {
            return Result<InventoryReservationOutcome>.Failure(new InventoryReservationOutcome(
                false, Guid.Empty, 0, 0, 0, string.Empty, string.Empty, string.Empty, "This variant is no longer available."));
        }

        var now = clock.UtcNow;

        // THE lock: one conditional UPDATE. Losing the race affects zero rows.
        var affected = await inventories.Query()
            .Where(i => i.Id == inventory.Id && i.AvailableQuantity - i.ReservedQuantity >= quantity)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(i => i.ReservedQuantity, i => i.ReservedQuantity + quantity)
                    .SetProperty(i => i.UpdatedAt, now),
                cancellationToken)
            .ConfigureAwait(false);

        if (affected == 0)
        {
            var fresh = await inventories.Query().AsNoTracking()
                .FirstAsync(i => i.Id == inventory.Id, cancellationToken)
                .ConfigureAwait(false);

            return Result<InventoryReservationOutcome>.Failure(new InventoryReservationOutcome(
                false,
                fresh.Id,
                fresh.AvailableQuantity,
                fresh.ReservedQuantity,
                fresh.SellableQuantity,
                await ProductNameAsync(fresh.ProductId, cancellationToken).ConfigureAwait(false),
                await VariantNameAsync(fresh.ProductVariantId, cancellationToken).ConfigureAwait(false),
                await SkuAsync(fresh.ProductVariantId, cancellationToken).ConfigureAwait(false),
                $"Only {fresh.SellableQuantity} unit(s) are available."));
        }

        var reservation = InventoryReservation.Create(
            inventory.Id,
            orderId,
            inventory.SellerId,
            quantity,
            now.AddMinutes(_options.ReservationMinutes),
            now);

        await reservations.AddAsync(reservation, cancellationToken).ConfigureAwait(false);

        var refreshed = await inventories.Query().AsNoTracking()
            .FirstAsync(i => i.Id == inventory.Id, cancellationToken)
            .ConfigureAwait(false);

        // A hold does not touch what is on hand, only what is reserved, so the row
        // brackets the reserved count: the value the conditional update started from.
        var reservedBefore = refreshed.ReservedQuantity + quantity;
        var ledger = InventoryTransaction.Record(
            refreshed,
            InventoryTransactionType.Reservation,
            quantity,
            reservedBefore,
            nameof(Domain.Orders.Order),
            orderId,
            "checkout",
            currentUser.UserId,
            now);
        await transactions.AddAsync(ledger, cancellationToken).ConfigureAwait(false);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Reserved {Quantity} unit(s) of variant {VariantId} for order {OrderId}", quantity, variantId, orderId);

        return Result<InventoryReservationOutcome>.Success(new InventoryReservationOutcome(
            true,
            refreshed.Id,
            refreshed.AvailableQuantity,
            refreshed.ReservedQuantity,
            refreshed.SellableQuantity,
            await ProductNameAsync(refreshed.ProductId, cancellationToken).ConfigureAwait(false),
            await VariantNameAsync(refreshed.ProductVariantId, cancellationToken).ConfigureAwait(false),
            await SkuAsync(refreshed.ProductVariantId, cancellationToken).ConfigureAwait(false),
            null));
    }

    public async Task<Result> ReleaseReservationAsync(InventoryReservation reservation, string reason, CancellationToken cancellationToken = default)
    {
        if (reservation is null || reservation.IsReleased)
        {
            // Releasing twice is a no-op, which keeps the cleanup job safe to re-run.
            return Result.Success();
        }

        var now = clock.UtcNow;
        var inventory = await inventories.Query().FirstOrDefaultAsync(i => i.Id == reservation.InventoryId, cancellationToken).ConfigureAwait(false);
        if (inventory is null)
        {
            return Result.Failure("Inventory record not found.", ResultErrorCodes.NotFound);
        }

        if (inventory.ReservedQuantity < reservation.Quantity)
        {
            return Result.Failure("Reserved quantity is inconsistent; manual review required.");
        }

        var affected = await inventories.Query()
            .Where(i => i.Id == inventory.Id && i.ReservedQuantity >= reservation.Quantity)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(i => i.ReservedQuantity, i => i.ReservedQuantity - reservation.Quantity)
                    .SetProperty(i => i.UpdatedAt, now),
                cancellationToken)
            .ConfigureAwait(false);

        if (affected == 0)
        {
            return Result.Failure("The reservation was already released.");
        }

        reservation.Release(now, reason);

        // Releasing a hold moves the reserved count back down, so the row brackets that.
        var reservedBefore = inventory.ReservedQuantity;
        var ledger = InventoryTransaction.Record(
            inventory,
            InventoryTransactionType.ReservationRelease,
            -reservation.Quantity,
            reservedBefore,
            nameof(Domain.Orders.Order),
            reservation.OrderId,
            reason,
            currentUser.UserId,
            now);
        await transactions.AddAsync(ledger, cancellationToken).ConfigureAwait(false);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> CommitReservationAsync(InventoryReservation reservation, CancellationToken cancellationToken = default)
    {
        if (reservation is null)
        {
            return Result.Failure("Reservation not found.", ResultErrorCodes.NotFound);
        }

        var now = clock.UtcNow;
        var inventory = await inventories.Query().FirstOrDefaultAsync(i => i.Id == reservation.InventoryId, cancellationToken).ConfigureAwait(false);
        if (inventory is null)
        {
            return Result.Failure("Inventory record not found.", ResultErrorCodes.NotFound);
        }

        if (inventory.ReservedQuantity < reservation.Quantity)
        {
            return Result.Failure("Reserved quantity is inconsistent; manual review required.");
        }

        // Completing a sale takes the units off the shelf, so the row brackets what is on hand.
        var availableBefore = inventory.AvailableQuantity;
        inventory.CommitSale(reservation.Quantity, now);

        var ledger = InventoryTransaction.Record(
            inventory,
            InventoryTransactionType.Sale,
            -reservation.Quantity,
            availableBefore,
            nameof(Domain.Orders.Order),
            reservation.OrderId,
            "payment-settled",
            currentUser.UserId,
            now);
        await transactions.AddAsync(ledger, cancellationToken).ConfigureAwait(false);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<int> ReleaseExpiredReservationsAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var expired = await reservations.Query()
            .Where(r => r.ReleasedAt == null && r.ExpiresAt <= now)
            .OrderBy(r => r.ExpiresAt)
            .Take(500)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var released = 0;
        foreach (var reservation in expired)
        {
            var result = await ReleaseReservationAsync(reservation, "reservation-expired", cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                released++;
            }
        }

        if (released > 0)
        {
            logger.LogInformation("Released {Count} expired inventory reservation(s)", released);
        }

        return released;
    }

    private IQueryable<InventoryRecord> ScopedQuery()
    {
        var source = inventories.Query();

        if (!currentUser.IsAdmin && currentUser.SellerId is { } sellerId)
        {
            source = source.Where(i => i.SellerId == sellerId);
        }

        return source;
    }

    private async Task RaiseLowStockAsync(InventoryRecord inventory, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!inventory.IsLowStock)
        {
            return;
        }

        inventory.AddDomainEvent(new InventoryLowStockEvent(
            inventory.Id, inventory.ProductId, inventory.ProductVariantId, inventory.SellerId,
            inventory.SellableQuantity, inventory.LowStockThreshold, now));

        var seller = await sellers.GetByIdAsync(inventory.SellerId, cancellationToken).ConfigureAwait(false);
        if (seller is null)
        {
            return;
        }

        var productName = await ProductNameAsync(inventory.ProductId, cancellationToken).ConfigureAwait(false);
        await notificationService.NotifySellerAsync(
            seller.UserId,
            NotificationType.LowStock,
            "Low stock alert",
            $"\"{productName}\" is down to {inventory.SellableQuantity} unit(s).",
            "/seller/inventory",
            cancellationToken).ConfigureAwait(false);

        await realtime.InventoryLowAsync(seller.Id, new
        {
            inventory.Id,
            inventory.ProductId,
            inventory.ProductVariantId,
            inventory.SellableQuantity,
            inventory.LowStockThreshold,
            Product = productName
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ProductNameAsync(Guid productId, CancellationToken cancellationToken) =>
        await products.Query().AsNoTracking().Where(p => p.Id == productId).Select(p => p.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;

    private async Task<string> VariantNameAsync(Guid variantId, CancellationToken cancellationToken) =>
        await variants.Query().AsNoTracking().Where(v => v.Id == variantId).Select(v => v.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;

    private async Task<string> SkuAsync(Guid variantId, CancellationToken cancellationToken) =>
        await variants.Query().AsNoTracking().Where(v => v.Id == variantId).Select(v => v.Sku).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;

    private async Task<List<InventoryItemResponse>> HydrateAsync(List<InventoryItemResponse> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return items;
        }

        var productIds = items.Select(i => i.ProductId).Distinct().ToList();
        var variantIds = items.Select(i => i.ProductVariantId).Distinct().ToList();

        var productInfo = await products.Query().AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, Url = p.Images.Where(i => i.IsPrimary).Select(i => i.Url).FirstOrDefault() ?? p.Images.Select(i => i.Url).FirstOrDefault() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var variantInfo = await variants.Query().AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Name, v.Sku })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var productMap = productInfo.ToDictionary(p => p.Id);
        var variantMap = variantInfo.ToDictionary(v => v.Id);

        for (var i = 0; i < items.Count; i++)
        {
            var product = productMap.GetValueOrDefault(items[i].ProductId);
            var variant = variantMap.GetValueOrDefault(items[i].ProductVariantId);
            items[i] = items[i] with
            {
                ProductName = product?.Name ?? string.Empty,
                ProductImageUrl = product?.Url,
                VariantName = variant?.Name ?? string.Empty,
                Sku = variant?.Sku ?? string.Empty
            };
        }

        return items;
    }
}