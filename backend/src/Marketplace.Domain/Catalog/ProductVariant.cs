using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

/// <summary>
/// A purchasable variant of a product (colour/size combination). Each variant owns its
/// own inventory record, which is what makes per-SKU stock control possible.
/// </summary>
public class ProductVariant : Entity
{
    private readonly List<ProductVariantOption> _options = [];

    private ProductVariant()
    {
        Sku = string.Empty;
        Name = string.Empty;
    }

    private ProductVariant(Guid id, Guid productId, string sku, string name, decimal price, int sortOrder, DateTimeOffset now)
        : base(id)
    {
        ProductId = productId;
        Sku = sku;
        Name = name;
        Price = price;
        SortOrder = sortOrder;
        IsActive = true;
        CreatedAt = now;
    }

    public Guid ProductId { get; private set; }

    /// <summary>Owning product. Navigation only - set by EF Core.</summary>
    public Product? Product { get; internal set; }

    public string Sku { get; private set; }

    public string Name { get; private set; }

    public decimal Price { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<ProductVariantOption> Options => _options.AsReadOnly();

    public static ProductVariant Create(Guid productId, string sku, string name, decimal price, int sortOrder, DateTimeOffset now)
    {
        Guard.NotEmpty(productId, nameof(productId));
        Guard.NotNullOrWhiteSpace(sku, nameof(sku));
        Guard.NotNullOrWhiteSpace(name, nameof(name));
        Guard.InRange(price, 0.01m, 10_000_000m, nameof(price));

        return new ProductVariant(SequentialGuid.New(now), productId, sku.Trim().ToUpperInvariant(), name.Trim(), price, sortOrder, now);
    }

    public void Update(string name, decimal price, bool isActive, DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(name, nameof(name));
        Guard.InRange(price, 0.01m, 10_000_000m, nameof(price));

        Name = name.Trim();
        Price = price;
        IsActive = isActive;
        UpdatedAt = now;
    }

    public void ChangeSku(string sku, DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(sku, nameof(sku));
        Sku = sku.Trim().ToUpperInvariant();
        UpdatedAt = now;
    }

    public void AddOption(string name, string value, DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(name, nameof(name));
        Guard.NotNullOrWhiteSpace(value, nameof(value));

        if (_options.Any(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase) &&
                              string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _options.Add(ProductVariantOption.Create(Id, name.Trim(), value.Trim(), now));
    }
}

public class ProductVariantOption : Entity
{
    private ProductVariantOption()
    {
        Name = string.Empty;
        Value = string.Empty;
    }

    private ProductVariantOption(Guid id, Guid variantId, string name, string value, DateTimeOffset now)
        : base(id)
    {
        VariantId = variantId;
        Name = name;
        Value = value;
        CreatedAt = now;
    }

    public Guid VariantId { get; private set; }

    /// <summary>Owning variant. Navigation only - set by EF Core.</summary>
    public ProductVariant? Variant { get; internal set; }

    public string Name { get; private set; }

    public string Value { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    internal static ProductVariantOption Create(Guid variantId, string name, string value, DateTimeOffset now) =>
        new(SequentialGuid.New(now), variantId, name, value, now);
}
