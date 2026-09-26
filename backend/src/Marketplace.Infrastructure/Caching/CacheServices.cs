using Marketplace.Application.Modules.Auth.Abstractions;
using System.Collections.Concurrent;
using System.Text.Json;
using Marketplace.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marketplace.Infrastructure.Caching;

/// <summary>
/// Read-through cache with tag-based invalidation.
///
/// Backed by <see cref="IDistributedCache"/> (Redis in production). When Redis is
/// disabled or unreachable the container registers <see cref="InMemoryCacheService"/>
/// instead, so development and CI never depend on a running Redis.
/// </summary>
public sealed class DistributedCacheService(
    IDistributedCache cache,
    IOptions<RedisOptions> options,
    ILogger<DistributedCacheService> logger) : ICacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly RedisOptions _options = options.Value;

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = await cache.GetAsync(Prefix(key), cancellationToken).ConfigureAwait(false);
            if (payload is null || payload.Length == 0)
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(payload, SerializerOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A cache failure must never fail a request: fall through to the database.
            logger.LogWarning(ex, "Cache read failed for {Key}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, IReadOnlyCollection<string>? tags = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
            var entryOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl ?? TimeSpan.FromMinutes(_options.DefaultTtlMinutes)
            };

            await cache.SetAsync(Prefix(key), payload, entryOptions, cancellationToken).ConfigureAwait(false);

            if (tags is { Count: > 0 })
            {
                foreach (var tag in tags)
                {
                    await TrackTagAsync(key, tag, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache write failed for {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.RemoveAsync(Prefix(key), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache eviction failed for {Key}", key);
        }
    }

    public async Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        try
        {
            // Redis is scanned for the tag index; a full SCAN is acceptable because
            // invalidation happens on writes, not on the hot read path.
            var indexKey = Prefix(TagIndexKey(tag));
            var index = await cache.GetStringAsync(indexKey, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrEmpty(index))
            {
                return;
            }

            foreach (var key in index.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                await cache.RemoveAsync(Prefix(key), cancellationToken).ConfigureAwait(false);
            }

            await cache.RemoveAsync(indexKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Tag-based cache eviction failed for {Tag}", tag);
        }
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, IReadOnlyCollection<string>? tags = null, CancellationToken cancellationToken = default)
    {
        var cached = await GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var created = await factory(cancellationToken).ConfigureAwait(false);
        await SetAsync(key, created, ttl, tags, cancellationToken).ConfigureAwait(false);
        return created;
    }

    /// <summary>Appends a key to the tag index so a single mutation can evict the family.</summary>
    private async Task TrackTagAsync(string key, string tag, CancellationToken cancellationToken)
    {
        var indexKey = Prefix(TagIndexKey(tag));
        var existing = await cache.GetStringAsync(indexKey, cancellationToken).ConfigureAwait(false);
        var combined = string.IsNullOrEmpty(existing) ? key : $"{existing},{key}";
        await cache.SetStringAsync(indexKey, combined, new DistributedCacheEntryOptions(), cancellationToken).ConfigureAwait(false);
    }

    private string Prefix(string key) => $"{_options.InstancePrefix}{key}";

    private static string TagIndexKey(string tag) => $"__tags__:{tag}";


}

/// <summary>
/// Process-local cache with the same semantics as the Redis-backed one. Registered when
/// Redis is disabled, so the application is fully functional without it.
/// </summary>
public sealed class InMemoryCacheService(ILogger<InMemoryCacheService> logger) : ICacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, HashSet<string>> _tagIndex = new(StringComparer.Ordinal);

    private sealed record CacheEntry(byte[] Payload, DateTimeOffset ExpiresAt);

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (!_entries.TryGetValue(key, out var entry) || entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _entries.TryRemove(key, out _);
            return Task.FromResult<T?>(default);
        }

        try
        {
            return Task.FromResult(JsonSerializer.Deserialize<T>(entry.Payload, SerializerOptions));
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "In-memory cache deserialisation failed for {Key}", key);
            return Task.FromResult<T?>(default);
        }
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, IReadOnlyCollection<string>? tags = null, CancellationToken cancellationToken = default)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(ttl ?? TimeSpan.FromMinutes(10));
        _entries[key] = new CacheEntry(JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions), expiresAt);

        if (tags is not null)
        {
            foreach (var tag in tags)
            {
                var set = _tagIndex.GetOrAdd(tag, _ => new HashSet<string>(StringComparer.Ordinal));
                lock (set)
                {
                    set.Add(key);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        if (_tagIndex.TryRemove(tag, out var set))
        {
            lock (set)
            {
                foreach (var key in set)
                {
                    _entries.TryRemove(key, out _);
                }
            }
        }

        return Task.CompletedTask;
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, IReadOnlyCollection<string>? tags = null, CancellationToken cancellationToken = default)
    {
        var cached = await GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var created = await factory(cancellationToken).ConfigureAwait(false);
        await SetAsync(key, created, ttl, tags, cancellationToken).ConfigureAwait(false);
        return created;
    }
}
