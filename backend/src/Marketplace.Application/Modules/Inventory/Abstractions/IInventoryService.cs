using Marketplace.Domain.Inventory;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Notifications.DTOs;
using Marketplace.Domain.Enums;

namespace Marketplace.Application.Modules.Inventory.Abstractions;

public interface IInventoryService
{
    Task<PagedResult<InventoryItemResponse>> ListAsync(InventoryListQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryItemResponse>> GetLowStockAsync(CancellationToken cancellationToken = default);

    Task<Result<InventoryItemResponse>> AdjustAsync(Guid variantId, AdjustStockRequest request, CancellationToken cancellationToken = default);

    Task<Result> SetThresholdAsync(Guid variantId, SetThresholdRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryTransactionResponse>> GetTransactionsAsync(Guid variantId, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically holds stock for a checkout. Uses a single conditional UPDATE so two
    /// concurrent buyers can never both succeed on the last unit.
    /// </summary>
    Task<Result<InventoryReservationOutcome>> ReserveAsync(Guid variantId, int quantity, Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Releases a previously created reservation. Idempotent.</summary>
    Task<Result> ReleaseReservationAsync(InventoryReservation reservation, string reason, CancellationToken cancellationToken = default);

    /// <summary>Converts a reservation into a completed sale.</summary>
    Task<Result> CommitReservationAsync(InventoryReservation reservation, CancellationToken cancellationToken = default);

    Task<int> ReleaseExpiredReservationsAsync(CancellationToken cancellationToken = default);
}

public sealed record InventoryListQuery(
    int? Page,
    int? PageSize,
    string? Search,
    bool? LowStockOnly,
    bool? OutOfStockOnly,
    Guid? ProductId);

/// <summary>Result of a reservation attempt, carrying the figures checkout needs.</summary>
public sealed record InventoryReservationOutcome(
    bool IsReserved,
    Guid InventoryId,
    int AvailableQuantity,
    int ReservedQuantity,
    int SellableQuantity,
    string ProductName,
    string VariantName,
    string Sku,
    string? FailureReason);
