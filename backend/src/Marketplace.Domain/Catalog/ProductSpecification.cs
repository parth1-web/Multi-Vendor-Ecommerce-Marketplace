using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public class ProductSpecification : Entity
{
    private ProductSpecification()
    {
        Key = string.Empty;
        Value = string.Empty;
    }

    private ProductSpecification(Guid id, Guid productId, string key, string value, int sortOrder, DateTimeOffset now)
        : base(id)
    {
        ProductId = productId;
        Key = key;
        Value = value;
        SortOrder = sortOrder;
        CreatedAt = now;
    }

    public Guid ProductId { get; private set; }

    /// <summary>Owning product. Navigation only - set by EF Core.</summary>
    public Product? Product { get; internal set; }

    public string Key { get; private set; }

    public string Value { get; private set; }

    public int SortOrder { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static ProductSpecification Create(Guid productId, string key, string value, int sortOrder, DateTimeOffset now)
    {
        Guard.NotEmpty(productId, nameof(productId));
        Guard.NotNullOrWhiteSpace(key, nameof(key));
        Guard.NotNullOrWhiteSpace(value, nameof(value));

        return new ProductSpecification(SequentialGuid.New(now), productId, key, value, sortOrder, now);
    }

    public void Update(string value, int sortOrder, DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(value, nameof(value));
        Value = value;
        SortOrder = sortOrder;
        _ = now;
    }
}

public class Tag : Entity
{
    private Tag() => Name = string.Empty;

    private Tag(Guid id, string name, DateTimeOffset now) : base(id)
    {
        Name = name;
        CreatedAt = now;
    }

    public string Name { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Products carrying this tag.</summary>
    public List<Product> Products { get; private set; } = [];

    public static Tag Create(string name, DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(name, nameof(name));
        return new Tag(SequentialGuid.New(now), name.Trim(), now);
    }
}
