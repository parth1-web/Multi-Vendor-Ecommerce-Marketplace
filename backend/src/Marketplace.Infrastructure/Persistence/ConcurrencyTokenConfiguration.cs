using System.Security.Cryptography;
using Marketplace.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.Infrastructure.Persistence;

/// <summary>
/// Maps the optimistic-concurrency token carried by every mutable aggregate root.
/// </summary>
/// <remarks>
/// The token is stored as an ordinary column and a fresh value is stamped on insert and on
/// every update by <see cref="MarketplaceDbContext"/>. It is deliberately not mapped with
/// <c>IsRowVersion()</c>: that maps the property onto a provider system column, which no
/// database will fill in for a column the migration actually creates, so every insert would
/// fail the not-null constraint. Stamping the value in the change tracker instead keeps one
/// behaviour on every provider, and EF still emits
/// <c>UPDATE … WHERE RowVersion = @original</c> for genuine lost-update detection.
/// </remarks>
public static class ConcurrencyTokenConfiguration
{
    /// <summary>Name of the concurrency token property on every mutable aggregate root.</summary>
    public const string RowVersionPropertyName = "RowVersion";

    /// <summary>Length of a generated token, in bytes.</summary>
    private const int TokenLength = 8;

    /// <summary>Applies the concurrency mapping to an entity's token property.</summary>
    public static PropertyBuilder<byte[]> ConfigureRowVersion<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class =>
        builder.Property<byte[]>(RowVersionPropertyName).IsConcurrencyToken();

    /// <summary>Creates a new, random concurrency token.</summary>
    public static byte[] NewToken() => RandomNumberGenerator.GetBytes(TokenLength);
}
