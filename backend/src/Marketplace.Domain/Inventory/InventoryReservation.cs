using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Inventory;

/// <summary>
/// A time-boxed hold on stock created during checkout. Expiry releases it back to the
/// seller, so an abandoned checkout can never permanently strand inventory.
/// </summary>
public class InventoryReservation : Entity
{
    private InventoryReservation()
    {
    }

    private InventoryReservation(Guid id, Guid inventoryId, Guid orderId, Guid sellerId, int quantity, DateTimeOffset expiresAt, DateTimeOffset now)
        : base(id)
    {
        InventoryId = inventoryId;
        OrderId = orderId;
        SellerId = sellerId;
        Quantity = quantity;
        ExpiresAt = expiresAt;
        CreatedAt = now;
    }

    public Guid InventoryId { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid SellerId { get; private set; }

    public int Quantity { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ReleasedAt { get; private set; }

    public string? ReleaseReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsReleased => ReleasedAt is not null;

    public bool IsExpired(DateTimeOffset now) => !IsReleased && now >= ExpiresAt;

    public static InventoryReservation Create(Guid inventoryId, Guid orderId, Guid sellerId, int quantity, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        Guard.NotEmpty(inventoryId, nameof(inventoryId));
        Guard.NotEmpty(orderId, nameof(orderId));
        Guard.GreaterThanZero(quantity, nameof(quantity));

        return new InventoryReservation(SequentialGuid.New(now), inventoryId, orderId, sellerId, quantity, expiresAt, now);
    }

    /// <summary>Releases the hold. Calling twice is a no-op, so the cleanup job is safe to re-run.</summary>
    public void Release(DateTimeOffset now, string reason)
    {
        if (IsReleased)
        {
            return;
        }

        ReleasedAt = now;
        ReleaseReason = reason;
    }

    public void Extend(DateTimeOffset newExpiry, DateTimeOffset now)
    {
        if (IsReleased)
        {
            throw new BusinessRuleException("A released reservation cannot be extended.");
        }

        if (newExpiry <= now)
        {
            throw new ValidationException(nameof(newExpiry), "The new expiry must be in the future.");
        }

        ExpiresAt = newExpiry;
    }
}
