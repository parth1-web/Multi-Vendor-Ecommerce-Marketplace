using System.Text;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Domain.Common;

namespace Marketplace.Application.Common.Helpers;

/// <summary>Builds unique, stable slugs by appending a counter when a base slug is taken.</summary>
public sealed class SlugGenerator(IClock clock)
{
    private readonly IClock _clock = clock;

    /// <summary>
    /// Produces a slug for <paramref name="source"/>, appending <c>-2</c>, <c>-3</c> … until
    /// <paramref name="isTaken"/> reports it free. A random suffix is used as a last resort
    /// so the generator can never loop forever.
    /// </summary>
    public async Task<string> EnsureUniqueAsync(
        string source,
        Func<string, CancellationToken, Task<bool>> isTaken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(isTaken);

        var baseSlug = Slug.Generate(source);
        if (string.IsNullOrEmpty(baseSlug))
        {
            baseSlug = "item";
        }

        var candidate = baseSlug;
        var suffix = 2;

        while (await isTaken(candidate, cancellationToken).ConfigureAwait(false))
        {
            if (suffix > 500)
            {
                var token = Convert.ToHexString(SequentialGuid.New(_clock.UtcNow).ToByteArray());
                return $"{baseSlug}-{token[..6].ToLowerInvariant()}";
            }

            candidate = $"{baseSlug}-{suffix}";
            suffix++;
        }

        return candidate;
    }
}

/// <summary>Stable hashing helper used to build cache keys from query objects.</summary>
public static class CacheKeyHasher
{
    public static string Hash(params string?[] parts)
    {
        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            builder.Append(part?.Trim().ToLowerInvariant() ?? "-");
            builder.Append('|');
        }

        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes)[..24].ToLowerInvariant();
    }
}
