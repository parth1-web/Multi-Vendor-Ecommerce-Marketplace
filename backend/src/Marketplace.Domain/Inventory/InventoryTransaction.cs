using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Inventory;

/// <summary>
/// Append-only ledger of every stock movement. Nothing ever updates or deletes a row,
/// which is what makes an inventory dispute reconstructable.
/// </summary>
public class InventoryTransaction : Entity
{
    private InventoryTransaction()
    {
    }

    private InventoryTransaction(
        Guid id,
        Guid inventoryId,
        Guid productVariantId,
        Guid productId,
        Guid sellerId,
        InventoryTransactionType type,
        int quantityDelta,
        int quantityBefore,
        int quantityAfter,
        string? referenceType,
        Guid? referenceId,
        string? reason,
        Guid? performedByUserId,
        DateTimeOffset now)
        : base(id)
    {
        InventoryId = inventoryId;
        ProductVariantId = productVariantId;
        ProductId = productId;
        SellerId = sellerId;
        Type = type;
        QuantityDelta = quantityDelta;
        QuantityBefore = quantityBefore;
        QuantityAfter = quantityAfter;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
        Reason = reason;
        PerformedByUserId = performedByUserId;
        CreatedAt = now;
    }

    public Guid InventoryId { get; private set; }

    public Guid ProductVariantId { get; private set; }

    public Guid ProductId { get; private set; }

    public Guid SellerId { get; private set; }

    public InventoryTransactionType Type { get; private set; }

    /// <summary>Signed movement: negative for a reservation or sale, positive for a restock.</summary>
    public int QuantityDelta { get; private set; }

    public int QuantityBefore { get; private set; }

    public int QuantityAfter { get; private set; }

    /// <summary>Entity kind this movement relates to, e.g. <c>Order</c> or <c>Refund</c>.</summary>
    public string? ReferenceType { get; private set; }

    public Guid? ReferenceId { get; private set; }

    public string? Reason { get; private set; }

    public Guid? PerformedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static InventoryTransaction Record(
        Inventory inventory,
        InventoryTransactionType type,
        int quantityDelta,
        string? referenceType,
        Guid? referenceId,
        string? reason,
        Guid? performedByUserId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        return new InventoryTransaction(
            SequentialGuid.New(now),
            inventory.Id,
            inventory.ProductVariantId,
            inventory.ProductId,
            inventory.SellerId,
            type,
            quantityDelta,
            inventory.AvailableQuantity,
            inventory.AvailableQuantity + quantityDelta,
            referenceType,
            referenceId,
            reason,
            performedByUserId,
            now);
    }
}
