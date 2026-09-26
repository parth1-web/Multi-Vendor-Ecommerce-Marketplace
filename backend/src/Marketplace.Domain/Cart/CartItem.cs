using Marketplace.Domain.Common;

namespace Marketplace.Domain.Cart;

public class CartItem : Entity
{
    private CartItem()
    {
    }

    private CartItem(Guid id, Guid cartId, Guid productId, Guid productVariantId, Guid sellerId, int quantity, decimal unitPrice, DateTimeOffset now)
        : base(id)
    {
        CartId = cartId;
        ProductId = productId;
        ProductVariantId = productVariantId;
        SellerId = sellerId;
        Quantity = quantity;
        UnitPrice = unitPrice;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid CartId { get; private set; }

    /// <summary>Owning cart. Navigation only — set by EF Core.</summary>
    public Cart? Cart { get; internal set; }

    public Guid ProductId { get; private set; }

    public Guid ProductVariantId { get; private set; }

    /// <summary>Cached on the item so the cart can be grouped by seller without extra queries.</summary>
    public Guid SellerId { get; private set; }

    public int Quantity { get; private set; }

    /// <summary>Display price only â€” checkout recomputes every amount from the catalogue.</summary>
    public decimal UnitPrice { get; private set; }

    public bool SavedForLater { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public decimal LineTotal => decimal.Round(UnitPrice * Quantity, 2, MidpointRounding.AwayFromZero);

    public static CartItem Create(Guid cartId, Guid productId, Guid productVariantId, Guid sellerId, int quantity, decimal unitPrice, DateTimeOffset now)
    {
        Guard.NotEmpty(cartId, nameof(cartId));
        Guard.NotEmpty(productId, nameof(productId));
        Guard.NotEmpty(productVariantId, nameof(productVariantId));
        Guard.NotEmpty(sellerId, nameof(sellerId));
        Guard.GreaterThanZero(quantity, nameof(quantity));
        Guard.GreaterThanOrEqualToZero(unitPrice, nameof(unitPrice));

        return new CartItem(SequentialGuid.New(now), cartId, productId, productVariantId, sellerId, quantity, unitPrice, now);
    }

    public void IncreaseQuantity(int delta, DateTimeOffset now)
    {
        Guard.GreaterThanZero(delta, nameof(delta));
        Quantity += delta;
        UpdatedAt = now;
    }

    public void SetQuantity(int quantity, DateTimeOffset now)
    {
        Guard.GreaterThanZero(quantity, nameof(quantity));
        Quantity = quantity;
        UpdatedAt = now;
    }

    public void RefreshUnitPrice(decimal unitPrice, DateTimeOffset now)
    {
        UnitPrice = unitPrice;
        UpdatedAt = now;
    }

    public void ToggleSavedForLater(DateTimeOffset now)
    {
        SavedForLater = !SavedForLater;
        UpdatedAt = now;
    }
}
