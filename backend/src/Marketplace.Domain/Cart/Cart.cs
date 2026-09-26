using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Cart;

/// <summary>
/// A shopping cart, owned either by a signed-in customer or by an anonymous session.
/// Prices are captured at add-time for display but always re-validated server-side.
/// </summary>
public class Cart : Entity
{
    private readonly List<CartItem> _items = [];

    private Cart()
    {
    }

    private Cart(Guid id, CartOwnerType ownerType, Guid? userId, string? guestToken, DateTimeOffset now)
        : base(id)
    {
        OwnerType = ownerType;
        UserId = userId;
        GuestToken = guestToken;
        CreatedAt = now;
        LastActivityAt = now;
    }

    public CartOwnerType OwnerType { get; private set; }

    public Guid? UserId { get; private set; }

    /// <summary>Opaque cookie value that binds an anonymous cart to a browser session.</summary>
    public string? GuestToken { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastActivityAt { get; private set; }

    public IReadOnlyCollection<CartItem> Items => _items.AsReadOnly();

    public static Cart ForCustomer(Guid userId, DateTimeOffset now)
    {
        Guard.NotEmpty(userId, nameof(userId));
        return new Cart(SequentialGuid.New(now), CartOwnerType.Customer, userId, null, now);
    }

    public static Cart ForGuest(string guestToken, DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(guestToken, nameof(guestToken));
        return new Cart(SequentialGuid.New(now), CartOwnerType.Guest, null, guestToken.Trim(), now);
    }

    public void Touch(DateTimeOffset now) => LastActivityAt = now;

    public CartItem AddItem(Product product, ProductVariant variant, int quantity, decimal unitPrice, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(variant);
        Guard.GreaterThanZero(quantity, nameof(quantity));

        var existing = _items.FirstOrDefault(i => i.ProductVariantId == variant.Id);
        if (existing is not null)
        {
            existing.IncreaseQuantity(quantity, now);
            Touch(now);
            return existing;
        }

        var item = CartItem.Create(Id, product.Id, variant.Id, product.SellerId, quantity, unitPrice, now);
        _items.Add(item);
        Touch(now);
        return item;
    }

    public void UpdateQuantity(Guid itemId, int quantity, DateTimeOffset now)
    {
        Guard.GreaterThanZero(quantity, nameof(quantity));
        var item = FindItem(itemId);
        item.SetQuantity(quantity, now);
        Touch(now);
    }

    public void RemoveItem(Guid itemId, DateTimeOffset now)
    {
        var item = FindItem(itemId);
        _items.Remove(item);
        Touch(now);
    }

    public void ToggleSavedForLater(Guid itemId, DateTimeOffset now)
    {
        var item = FindItem(itemId);
        item.ToggleSavedForLater(now);
        Touch(now);
    }

    public void Clear(DateTimeOffset now)
    {
        _items.Clear();
        Touch(now);
    }

    /// <summary>Moves every item into a new guest cart after a guest signs in.</summary>
    public void TransferTo(Guid userId, DateTimeOffset now)
    {
        Guard.NotEmpty(userId, nameof(userId));
        OwnerType = CartOwnerType.Customer;
        UserId = userId;
        GuestToken = null;
        Touch(now);
    }

    public bool IsAbandoned(DateTimeOffset now, TimeSpan threshold) => _items.Count > 0 && now - LastActivityAt > threshold;

    private CartItem FindItem(Guid itemId) =>
        _items.FirstOrDefault(i => i.Id == itemId)
        ?? throw new ValidationException(nameof(itemId), "Item is not in this cart.");
}
