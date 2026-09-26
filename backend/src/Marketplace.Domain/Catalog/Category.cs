using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

/// <summary>
/// A node in the category tree. Parent/child relationships are validated here so the
/// public API can never be handed a cycle.
/// </summary>
public class Category : Entity
{
    private Category()
    {
        Name = string.Empty;
        SlugValue = string.Empty;
    }

    private Category(Guid id, string name, Slug slug, string? description, Guid? parentId, int displayOrder, DateTimeOffset now)
        : base(id)
    {
        Name = name;
        SlugValue = slug.Value;
        Description = description;
        ParentId = parentId;
        DisplayOrder = displayOrder;
        IsActive = true;
        CreatedAt = now;
    }

    public Guid? ParentId { get; private set; }

    public string Name { get; private set; }

    /// <summary>URL-safe identifier backing <c>/categories/{slug}</c>.</summary>
    public string SlugValue { get; private set; }

    public Slug Slug => Slug.FromExisting(SlugValue);

    public string? Description { get; private set; }

    public string? ImageUrl { get; private set; }

    public int DisplayOrder { get; private set; }

    public bool IsActive { get; private set; } = true;

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public int ProductCount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsRoot => ParentId is null;

    public static Category Create(string name, Slug slug, string? description, Guid? parentId, int displayOrder, DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(name, nameof(name));
        Guard.NotNull(slug, nameof(slug));

        return new Category(SequentialGuid.New(now), name.Trim(), slug, description?.Trim(), parentId, displayOrder, now);
    }

    public void Update(string name, string? description, string? imageUrl, int displayOrder, DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(name, nameof(name));

        Name = name.Trim();
        Description = description?.Trim();
        ImageUrl = imageUrl;
        DisplayOrder = displayOrder;
        UpdatedAt = now;
    }

    public void ChangeParent(Guid? parentId, Guid selfId, IReadOnlyCollection<Guid> descendantIds, DateTimeOffset now)
    {
        if (parentId == selfId)
        {
            throw new ValidationException(nameof(parentId), "A category cannot be its own parent.");
        }

        if (parentId is not null && descendantIds.Contains(parentId.Value))
        {
            throw new BusinessRuleException("A category cannot be moved under one of its own descendants.");
        }

        ParentId = parentId;
        UpdatedAt = now;
    }

    /// <summary>Slugs are part of the public URL contract, so they only change on explicit request.</summary>
    public void ChangeSlug(Slug slug, DateTimeOffset now)
    {
        Guard.NotNull(slug, nameof(slug));
        SlugValue = slug.Value;
        UpdatedAt = now;
    }

    public void SetActive(bool isActive, DateTimeOffset now)
    {
        IsActive = isActive;
        UpdatedAt = now;
    }

    public void Reorder(int displayOrder, DateTimeOffset now)
    {
        DisplayOrder = displayOrder;
        UpdatedAt = now;
    }

    public void RecalculateProductCount(int count, DateTimeOffset now)
    {
        ProductCount = Math.Max(0, count);
        UpdatedAt = now;
    }

    public void SoftDelete(DateTimeOffset now)
    {
        if (ProductCount > 0)
        {
            throw new BusinessRuleException("A category that still has products can only be deactivated, not deleted.");
        }

        IsDeleted = true;
        IsActive = false;
        DeletedAt = now;
        UpdatedAt = now;
    }
}
