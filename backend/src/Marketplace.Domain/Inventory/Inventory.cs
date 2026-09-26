using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Inventory;

/// <summary>
/// Per-variant stock. Three counters are tracked separately so reserved-but-unsold
/// stock cannot be spent twice, and the available figure is always derivable.
/// </summary>
public class Inventory : Entity
{
    private Inventory()
    {
    }

    private Inventory(Guid id, Guid productVariantId, Guid productId, Guid sellerId, int availableQuantity, int lowStockThreshold, DateTimeOffset now)
        : base(id)
    {
        ProductVariantId = productVariantId;
        ProductId = productId;
        SellerId = sellerId;
        AvailableQuantity = availableQuantity;
        LowStockThreshold = Math.Max(0, lowStockThreshold);
        CreatedAt = now;
    }

    public Guid ProductVariantId { get; private set; }

    public Guid ProductId { get; private set; }

    public Guid SellerId { get; private set; }

    /// <summary>Physically on hand and free to sell.</summary>
    public int AvailableQuantity { get; private set; }

    /// <summary>Held for a checkout that has not completed or expired yet.</summary>
    public int ReservedQuantity { get; private set; }

    /// <summary>Already sold and shipped.</summary>
    public int SoldQuantity { get; private set; }

    public int LowStockThreshold { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmax</c>).</summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Stock a new order can draw from: physical stock minus what is already held.</summary>
    public int SellableQuantity => AvailableQuantity - ReservedQuantity;

    public bool IsOutOfStock => SellableQuantity <= 0;

    public bool IsLowStock => SellableQuantity <= LowStockThreshold;

    public static Inventory Create(Guid productVariantId, Guid productId, Guid sellerId, int availableQuantity, int lowStockThreshold, DateTimeOffset now)
    {
        Guard.NotEmpty(productVariantId, nameof(productVariantId));
        Guard.NotEmpty(productId, nameof(productId));
        Guard.NotEmpty(sellerId, nameof(sellerId));
        Guard.GreaterThanOrEqualToZero(availableQuantity, nameof(availableQuantity));

        return new Inventory(SequentialGuid.New(now), productVariantId, productId, sellerId, availableQuantity, lowStockThreshold, now);
    }

    /// <summary>
    /// Moves stock into the reserved bucket. Must be called only from inside a
    /// transaction that also writes an <see cref="InventoryTransaction"/>; the caller is
    /// responsible for the conditional-UPDATE concurrency guard.
    /// </summary>
    public void Reserve(int quantity, DateTimeOffset now)
    {
        Guard.GreaterThanZero(quantity, nameof(quantity));

        if (quantity > SellableQuantity)
        {
            throw new InsufficientStockException($"variant {ProductVariantId}", quantity, SellableQuantity);
        }

        ReservedQuantity += quantity;
        UpdatedAt = now;
    }

    /// <summary>Returns held stock to the available bucket. Idempotent by construction of the caller.</summary>
    public void ReleaseReservation(int quantity, DateTimeOffset now)
    {
        Guard.GreaterThanZero(quantity, nameof(quantity));

        if (quantity > ReservedQuantity)
        {
            throw new BusinessRuleException($"Cannot release {quantity}: only {ReservedQuantity} unit(s) are reserved.");
        }

        ReservedQuantity -= quantity;
        UpdatedAt = now;
    }

    /// <summary>Converts a reservation into a completed sale.</summary>
    public void CommitSale(int quantity, DateTimeOffset now)
    {
        Guard.GreaterThanZero(quantity, nameof(quantity));

        if (quantity > ReservedQuantity)
        {
            throw new BusinessRuleException($"Cannot complete a sale of {quantity}: only {ReservedQuantity} unit(s) are reserved.");
        }

        ReservedQuantity -= quantity;
        AvailableQuantity -= quantity;
        SoldQuantity += quantity;
        UpdatedAt = now;
    }

    /// <summary>Cancels a sale: stock goes back on the shelf.</summary>
    public void ReverseSale(int quantity, DateTimeOffset now)
    {
        Guard.GreaterThanZero(quantity, nameof(quantity));

        if (quantity > SoldQuantity)
        {
            throw new BusinessRuleException($"Cannot reverse {quantity}: only {SoldQuantity} unit(s) were sold.");
        }

        SoldQuantity -= quantity;
        AvailableQuantity += quantity;
        UpdatedAt = now;
    }

    /// <summary>Manual seller restock.</summary>
    public void Restock(int quantity, DateTimeOffset now)
    {
        Guard.GreaterThanZero(quantity, nameof(quantity));
        AvailableQuantity += quantity;
        UpdatedAt = now;
    }

    /// <summary>Manual seller adjustment, positive or negative (shrinkage, damage).</summary>
    public void Adjust(int delta, DateTimeOffset now)
    {
        if (delta == 0)
        {
            throw new ValidationException(nameof(delta), "Adjustment cannot be zero.");
        }

        if (delta < 0 && Math.Abs(delta) > AvailableQuantity)
        {
            throw new BusinessRuleException($"Cannot remove {Math.Abs(delta)}: only {AvailableQuantity} unit(s) on hand.");
        }

        AvailableQuantity += delta;
        UpdatedAt = now;
    }

    /// <summary>Customer return: goods come back on the shelf.</summary>
    public void ReceiveReturn(int quantity, DateTimeOffset now)
    {
        Guard.GreaterThanZero(quantity, nameof(quantity));
        AvailableQuantity += quantity;
        UpdatedAt = now;
    }

    public void SetLowStockThreshold(int threshold, DateTimeOffset now)
    {
        LowStockThreshold = Math.Max(0, threshold);
        UpdatedAt = now;
    }
}
