using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public class ProductImage : Entity
{
    private ProductImage()
    {
        Url = string.Empty;
    }

    private ProductImage(Guid id, Guid productId, string url, string? altText, bool isPrimary, int sortOrder, DateTimeOffset now)
        : base(id)
    {
        ProductId = productId;
        Url = url;
        AltText = altText;
        IsPrimary = isPrimary;
        SortOrder = sortOrder;
        CreatedAt = now;
    }

    public Guid ProductId { get; private set; }

    /// <summary>Owning product. Navigation only - set by EF Core.</summary>
    public Product? Product { get; internal set; }

    public string Url { get; private set; }

    /// <summary>Drives the <c>alt</c> attribute; falls back to the product name when empty.</summary>
    public string? AltText { get; private set; }

    public bool IsPrimary { get; private set; }

    public int SortOrder { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static ProductImage Create(Guid productId, string url, string? altText, bool isPrimary, int sortOrder, DateTimeOffset now)
    {
        Guard.NotEmpty(productId, nameof(productId));
        Guard.NotNullOrWhiteSpace(url, nameof(url));

        return new ProductImage(SequentialGuid.New(now), productId, url.Trim(), altText?.Trim(), isPrimary, sortOrder, now);
    }

    public void Update(string url, string? altText, DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(url, nameof(url));
        Url = url.Trim();
        AltText = altText?.Trim();
        _ = now;
    }

    public void SetPrimary() => IsPrimary = true;

    public void ClearPrimary() => IsPrimary = false;
}
