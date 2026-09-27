namespace Marketplace.Application.Common.Interfaces;

/// <summary>Read-through/write-through cache with tagged invalidation.</summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, IReadOnlyCollection<string>? tags = null, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Removes every key registered under a tag — used when a mutation invalidates a family of entries.</summary>
    Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default);

    /// <summary>Returns the cached value or invokes <paramref name="factory"/> and stores the result.</summary>
    Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, IReadOnlyCollection<string>? tags = null, CancellationToken cancellationToken = default);
}

/// <summary>Canonical cache keys and tags. Keeping them in one place stops key drift.</summary>
public static class CacheKeys
{
    public const string CatalogTag = "catalog";
    public const string CategoryTag = "categories";
    public const string StoreProfileTag = "stores";
    public const string HomeTag = "home";

    public static string ProductList(string hash) => $"catalog:products:{hash}";

    public static string Product(Guid id) => $"catalog:product:{id:N}";

    public static string ProductBySlug(string slug) => $"catalog:product:slug:{slug}";

    // A tag key must not collide with the entry key it invalidates: both were "catalog:product:{id}",
    // which made a tag sweep indistinguishable from reading or writing the detail itself.
    public static string ProductTag(Guid id) => $"catalog:tag:product:{id:N}";

    public static string SellerProductTag(Guid sellerId) => $"catalog:seller:{sellerId:N}";

    public static string Categories() => "categories:tree";

    public static string Category(string slug) => $"categories:{slug}";

    public static string StoreBySlug(string slug) => $"stores:profile:{slug}";

    public static string StoreTag(Guid sellerId) => $"stores:seller:{sellerId:N}";

    public static string Home() => "home:payload";
}
