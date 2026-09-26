using Marketplace.Domain.Common;

namespace Marketplace.Domain.Sellers;

/// <summary>
/// Public storefront profile of a seller: the SEO-facing identity of the store.
/// </summary>
public class SellerStore : Entity
{
    private SellerStore()
    {
        Name = string.Empty;
        SlugValue = string.Empty;
        Description = string.Empty;
    }

    private SellerStore(Guid id, Guid sellerId, string name, Slug slug, string description, DateTimeOffset now)
        : base(id)
    {
        SellerId = sellerId;
        Name = name;
        SlugValue = slug.Value;
        Description = description;
        CreatedAt = now;
    }

    public Guid SellerId { get; private set; }

    public string Name { get; private set; }

    /// <summary>URL-safe identifier backing <c>/stores/{slug}</c>.</summary>
    public string SlugValue { get; private set; }

    public Slug Slug => Slug.FromExisting(SlugValue);

    public string Description { get; private set; }

    public string? LogoUrl { get; private set; }

    public string? BannerUrl { get; private set; }

    public string? SupportEmail { get; private set; }

    public string? SupportPhone { get; private set; }

    public string? ReturnPolicy { get; private set; }

    public string? ShippingPolicy { get; private set; }

    public int? FoundedYear { get; private set; }

    public decimal RatingAverage { get; private set; }

    public int RatingCount { get; private set; }

    public bool IsActive { get; private set; } = true;

    public int ProductCount { get; private set; }

    public int TotalSalesCount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static SellerStore Create(Guid sellerId, string name, Slug slug, string description, DateTimeOffset now)
    {
        Guard.NotEmpty(sellerId, nameof(sellerId));
        Guard.NotNullOrWhiteSpace(name, nameof(name));
        Guard.NotNull(slug, nameof(slug));

        return new SellerStore(SequentialGuid.New(now), sellerId, name.Trim(), slug, description?.Trim() ?? string.Empty, now);
    }

    public void UpdateProfile(
        string name,
        string? description,
        string? logoUrl,
        string? bannerUrl,
        string? supportEmail,
        string? supportPhone,
        string? returnPolicy,
        string? shippingPolicy,
        int? foundedYear,
        DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(name, nameof(name));

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        LogoUrl = logoUrl;
        BannerUrl = bannerUrl;
        SupportEmail = supportEmail?.Trim();
        SupportPhone = supportPhone?.Trim();
        ReturnPolicy = returnPolicy?.Trim();
        ShippingPolicy = shippingPolicy?.Trim();
        FoundedYear = foundedYear is >= 1800 and <= 2100 ? foundedYear : null;
        UpdatedAt = now;
    }

    /// <summary>Rating aggregates are recomputed from visible reviews; never trusted from a request.</summary>
    public void RecalculateRating(decimal average, int count, DateTimeOffset now)
    {
        RatingAverage = decimal.Round(count == 0 ? 0m : average, 2, MidpointRounding.AwayFromZero);
        RatingCount = count;
        UpdatedAt = now;
    }

    public void UpdateProductCount(int count, DateTimeOffset now)
    {
        ProductCount = Math.Max(0, count);
        UpdatedAt = now;
    }

    public void RecordSale(DateTimeOffset now)
    {
        TotalSalesCount++;
        UpdatedAt = now;
    }

    public void SetActive(bool isActive, DateTimeOffset now)
    {
        IsActive = isActive;
        UpdatedAt = now;
    }
}
