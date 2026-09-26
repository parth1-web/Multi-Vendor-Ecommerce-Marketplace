using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Catalog;

/// <summary>
/// A listed product. Owns its images, variants, specifications and tags. Approval state
/// lives here so an unapproved product can never leak into a public query.
/// </summary>
public class Product : Entity
{
    private readonly List<ProductImage> _images = [];
    private readonly List<ProductVariant> _variants = [];
    private readonly List<ProductSpecification> _specifications = [];
    private readonly List<Tag> _tags = [];

    private Product()
    {
        Name = string.Empty;
        SlugValue = string.Empty;
        Description = string.Empty;
        ShortDescription = string.Empty;
    }

    private Product(Guid id, Guid sellerId, Guid categoryId, string name, Slug slug, string shortDescription, string description, decimal basePrice, decimal? compareAtPrice, string? brand, string? model, DateTimeOffset now)
        : base(id)
    {
        SellerId = sellerId;
        CategoryId = categoryId;
        Name = name;
        SlugValue = slug.Value;
        ShortDescription = shortDescription;
        Description = description;
        BasePrice = basePrice;
        CompareAtPrice = compareAtPrice;
        Brand = brand;
        Model = model;
        Status = ProductStatus.Draft;
        CreatedAt = now;
    }

    public Guid SellerId { get; private set; }

    public Guid CategoryId { get; private set; }

    public string Name { get; private set; }

    /// <summary>URL-safe identifier backing <c>/products/{slug}</c>.</summary>
    public string SlugValue { get; private set; }

    public Slug Slug => Slug.FromExisting(SlugValue);

    public string ShortDescription { get; private set; }

    public string Description { get; private set; }

    public decimal BasePrice { get; private set; }

    /// <summary>Original price shown struck through next to a discounted price.</summary>
    public decimal? CompareAtPrice { get; private set; }

    public string? Brand { get; private set; }

    public string? Model { get; private set; }

    public ProductStatus Status { get; private set; }

    public ProductRejectionReason RejectionReason { get; private set; } = ProductRejectionReason.None;

    public string? RejectionNote { get; private set; }

    public bool IsFeatured { get; private set; }

    public int ViewCount { get; private set; }

    public int SoldCount { get; private set; }

    public decimal RatingAverage { get; private set; }

    public int RatingCount { get; private set; }

    public int ReviewCount { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmax</c>).</summary>
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<ProductImage> Images => _images.AsReadOnly();

    public IReadOnlyCollection<ProductVariant> Variants => _variants.AsReadOnly();

    public IReadOnlyCollection<ProductSpecification> Specifications => _specifications.AsReadOnly();

    public IReadOnlyCollection<Tag> Tags => _tags.AsReadOnly();

    public bool IsPubliclyVisible => Status == ProductStatus.Published && !IsDeleted;

    public int DiscountPercentage => CompareAtPrice is null || CompareAtPrice <= 0m || CompareAtPrice <= BasePrice
        ? 0
        : (int)Math.Round((CompareAtPrice.Value - BasePrice) / CompareAtPrice.Value * 100m, MidpointRounding.AwayFromZero);

    public static Product Create(
        Guid sellerId,
        Guid categoryId,
        string name,
        Slug slug,
        string shortDescription,
        string description,
        decimal basePrice,
        decimal? compareAtPrice,
        string? brand,
        string? model,
        DateTimeOffset now)
    {
        Guard.NotEmpty(sellerId, nameof(sellerId));
        Guard.NotEmpty(categoryId, nameof(categoryId));
        Guard.NotNullOrWhiteSpace(name, nameof(name));
        Guard.NotNull(slug, nameof(slug));
        Guard.InRange(basePrice, 0.01m, 10_000_000m, nameof(basePrice));

        if (compareAtPrice is not null && compareAtPrice <= basePrice)
        {
            throw new ValidationException(nameof(compareAtPrice), "The compare-at price must be higher than the selling price.");
        }

        return new Product(
            SequentialGuid.New(now),
            sellerId,
            categoryId,
            name.Trim(),
            slug,
            shortDescription?.Trim() ?? string.Empty,
            description?.Trim() ?? string.Empty,
            basePrice,
            compareAtPrice,
            brand?.Trim(),
            model?.Trim(),
            now);
    }

    public void UpdateDetails(string name, string shortDescription, string description, Guid categoryId, string? brand, string? model, DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(name, nameof(name));
        Guard.NotEmpty(categoryId, nameof(categoryId));

        Name = name.Trim();
        ShortDescription = shortDescription?.Trim() ?? string.Empty;
        Description = description?.Trim() ?? string.Empty;
        CategoryId = categoryId;
        Brand = brand?.Trim();
        Model = model?.Trim();

        // Editing an approved product sends it back through moderation.
        if (Status == ProductStatus.Published)
        {
            Status = ProductStatus.PendingApproval;
        }

        UpdatedAt = now;
    }

    public void UpdatePricing(decimal basePrice, decimal? compareAtPrice, DateTimeOffset now)
    {
        Guard.InRange(basePrice, 0.01m, 10_000_000m, nameof(basePrice));

        if (compareAtPrice is not null && compareAtPrice <= basePrice)
        {
            throw new ValidationException(nameof(compareAtPrice), "The compare-at price must be higher than the selling price.");
        }

        BasePrice = basePrice;
        CompareAtPrice = compareAtPrice;
        UpdatedAt = now;
    }

    public void ChangeSlug(Slug slug, DateTimeOffset now)
    {
        Guard.NotNull(slug, nameof(slug));
        SlugValue = slug.Value;
        UpdatedAt = now;
    }

    public void SubmitForApproval(DateTimeOffset now)
    {
        if (_images.Count == 0)
        {
            throw new BusinessRuleException("At least one product image is required before submitting for approval.");
        }

        if (_variants.Count == 0)
        {
            throw new BusinessRuleException("At least one product variant is required before submitting for approval.");
        }

        Status = ProductStatus.PendingApproval;
        RejectionReason = ProductRejectionReason.None;
        RejectionNote = null;
        UpdatedAt = now;
    }

    public void Approve(DateTimeOffset now)
    {
        if (Status is not (ProductStatus.PendingApproval or ProductStatus.Draft))
        {
            throw new InvalidStateTransitionException(nameof(Product), Status.ToString(), ProductStatus.Published.ToString());
        }

        Status = ProductStatus.Published;
        PublishedAt ??= now;
        RejectionReason = ProductRejectionReason.None;
        RejectionNote = null;
        UpdatedAt = now;
    }

    public void Reject(ProductRejectionReason reason, string note, DateTimeOffset now)
    {
        if (Status is not (ProductStatus.PendingApproval or ProductStatus.Published))
        {
            throw new InvalidStateTransitionException(nameof(Product), Status.ToString(), ProductStatus.Rejected.ToString());
        }

        Status = ProductStatus.Rejected;
        RejectionReason = reason;
        RejectionNote = note?.Trim();
        UpdatedAt = now;
    }

    public void SetFeatured(bool isFeatured, DateTimeOffset now)
    {
        if (isFeatured && !IsPubliclyVisible)
        {
            throw new BusinessRuleException("Only a published product can be featured.");
        }

        IsFeatured = isFeatured;
        UpdatedAt = now;
    }

    public void Archive(DateTimeOffset now)
    {
        Status = ProductStatus.Archived;
        IsFeatured = false;
        UpdatedAt = now;
    }

    public void RecordView(DateTimeOffset now)
    {
        ViewCount++;
        UpdatedAt = now;
    }

    public void RecordSale(int quantity, DateTimeOffset now)
    {
        if (quantity <= 0)
        {
            return;
        }

        SoldCount += quantity;
        UpdatedAt = now;
    }

    public void RecalculateRating(decimal average, int ratingCount, int reviewCount, DateTimeOffset now)
    {
        RatingAverage = decimal.Round(ratingCount == 0 ? 0m : average, 2, MidpointRounding.AwayFromZero);
        RatingCount = ratingCount;
        ReviewCount = reviewCount;
        UpdatedAt = now;
    }

    public void SoftDelete(DateTimeOffset now)
    {
        IsDeleted = true;
        IsFeatured = false;
        DeletedAt = now;
        UpdatedAt = now;
    }

    // ---- images -------------------------------------------------------------

    public ProductImage AddImage(string url, string? altText, bool isPrimary, DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(url, nameof(url));

        if (_images.Count == 0)
        {
            isPrimary = true;
        }
        else if (isPrimary)
        {
            foreach (var existing in _images)
            {
                existing.ClearPrimary();
            }
        }

        var newImage = ProductImage.Create(Id, url, altText, isPrimary, _images.Count, now);
        _images.Add(newImage);
        UpdatedAt = now;
        return newImage;
    }

    public void RemoveImage(Guid imageId, DateTimeOffset now)
    {
        var image = _images.FirstOrDefault(i => i.Id == imageId)
            ?? throw new ValidationException(nameof(imageId), "Image not found on this product.");

        _images.Remove(image);
        if (image.IsPrimary && _images.Count > 0)
        {
            _images[0].SetPrimary();
        }

        UpdatedAt = now;
    }

    // ---- variants -----------------------------------------------------------

    public ProductVariant AddVariant(string sku, string name, decimal? price, int sortOrder, DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(sku, nameof(sku));
        Guard.NotNullOrWhiteSpace(name, nameof(name));

        if (price is not null)
        {
            Guard.InRange(price.Value, 0.01m, 10_000_000m, nameof(price));
        }

        if (_variants.Any(v => string.Equals(v.Sku, sku, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DuplicateEntityException(nameof(ProductVariant), nameof(sku), sku);
        }

        var variant = ProductVariant.Create(Id, sku, name, price ?? BasePrice, sortOrder, now);
        _variants.Add(variant);
        UpdatedAt = now;
        return variant;
    }

    public void RemoveVariant(Guid variantId, DateTimeOffset now)
    {
        var variant = _variants.FirstOrDefault(v => v.Id == variantId)
            ?? throw new ValidationException(nameof(variantId), "Variant not found on this product.");

        _variants.Remove(variant);
        UpdatedAt = now;
    }

    // ---- specifications & tags ---------------------------------------------

    public void AddSpecification(string key, string value, int sortOrder, DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(key, nameof(key));
        Guard.NotNullOrWhiteSpace(value, nameof(value));

        var existing = _specifications.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Update(value, sortOrder, now);
        }
        else
        {
            _specifications.Add(ProductSpecification.Create(Id, key.Trim(), value.Trim(), sortOrder, now));
        }

        UpdatedAt = now;
    }

    public void AddTag(string name, DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(name, nameof(name));
        var trimmed = name.Trim();
        if (_tags.Any(t => string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _tags.Add(Tag.Create(trimmed, now));
        UpdatedAt = now;
    }

    /// <summary>
    /// Attaches tags that already exist in the catalogue's shared tag list.
    /// </summary>
    /// <remarks>
    /// A tag name is unique across the whole marketplace and many products share one row, so
    /// creating a second row with the same name would break the join. Callers resolve the
    /// existing rows first and pass them in; only genuinely new names need new rows.
    /// </remarks>
    public void AttachTags(IEnumerable<Tag> tags, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tags);

        foreach (var tag in tags)
        {
            if (_tags.Any(t => t.Id == tag.Id || string.Equals(t.Name, tag.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _tags.Add(tag);
        }

        UpdatedAt = now;
    }
}
