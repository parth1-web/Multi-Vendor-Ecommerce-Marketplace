using Marketplace.Domain.Common;

namespace Marketplace.Domain.Cart;

/// <summary>A customer's saved list. Kept as its own aggregate so it never couples to the cart.</summary>
public class Wishlist : Entity
{
    private readonly List<WishlistItem> _items = [];

    private Wishlist()
    {
    }

    private Wishlist(Guid id, Guid userId, DateTimeOffset now) : base(id)
    {
        UserId = userId;
        CreatedAt = now;
    }

    public Guid UserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<WishlistItem> Items => _items.AsReadOnly();

    public static Wishlist ForUser(Guid userId, DateTimeOffset now)
    {
        Guard.NotEmpty(userId, nameof(userId));
        return new Wishlist(SequentialGuid.New(now), userId, now);
    }

    public bool Add(Guid productId, DateTimeOffset now)
    {
        Guard.NotEmpty(productId, nameof(productId));
        if (_items.Any(i => i.ProductId == productId))
        {
            return false;
        }

        _items.Add(WishlistItem.Create(Id, productId, now));
        return true;
    }

    public void Remove(Guid productId, DateTimeOffset now)
    {
        var item = _items.FirstOrDefault(i => i.ProductId == productId)
            ?? throw new ValidationException(nameof(productId), "Product is not on the wishlist.");
        _items.Remove(item);
        _ = now;
    }

    public void Clear()
    {
        _items.Clear();
    }

    public void RemoveMany(IEnumerable<Guid> productIds, DateTimeOffset now)
    {
        var ids = productIds.ToHashSet();
        _items.RemoveAll(i => ids.Contains(i.ProductId));
        _ = now;
    }
}

public class WishlistItem : Entity
{
    private WishlistItem()
    {
    }

    private WishlistItem(Guid id, Guid wishlistId, Guid productId, DateTimeOffset now) : base(id)
    {
        WishlistId = wishlistId;
        ProductId = productId;
        AddedAt = now;
    }

    public Guid WishlistId { get; private set; }

    /// <summary>Owning wishlist. Navigation only — set by EF Core.</summary>
    public Wishlist? Wishlist { get; internal set; }

    public Guid ProductId { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    internal static WishlistItem Create(Guid wishlistId, Guid productId, DateTimeOffset now) =>
        new(SequentialGuid.New(now), wishlistId, productId, now);
}
