using Marketplace.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.Infrastructure.Persistence;

/// <summary>
/// Configures optimistic-concurrency tokens in a provider-aware way.
/// </summary>
/// <remarks>
/// Production runs PostgreSQL, where <c>IsRowVersion()</c> maps the token to the
/// <c>xmax</c> system column and EF issues
/// <c>UPDATE … WHERE rowversion = @p</c>, giving true lost-update detection.
///
/// Providers without a native rowversion (SQLite, used by the integration suite) get a
/// plain concurrency token with a blob default so the schema is still valid; the token is
/// not auto-mutated there, so concurrency is exercised only on PostgreSQL. Everything the
/// test suite asserts about correctness — conditional UPDATEs, unique indexes, check
/// constraints and transactions — behaves identically on both.
/// </remarks>
public static class ConcurrencyTokenConfiguration
{
    /// <summary>Name of the concurrency token property on every mutable aggregate root.</summary>
    public const string RowVersionPropertyName = "RowVersion";

    /// <summary>Applies the right concurrency mapping for the current provider.</summary>
    public static PropertyBuilder<byte[]> ConfigureRowVersion<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        var providerName = MarketplaceDbContext.CurrentProviderName;
        var property = builder.Property<byte[]>(RowVersionPropertyName);

        if (IsPostgres(providerName))
        {
            return property.IsRowVersion();
        }

        // No native rowversion: keep the value EF-managed and treat it as a plain
        // concurrency token. Correctness on this provider comes from conditional UPDATEs,
        // unique indexes and transactions rather than from the token.
        return property.IsConcurrencyToken();
    }

    private static bool IsPostgres(string? providerName) =>
        providerName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
}
