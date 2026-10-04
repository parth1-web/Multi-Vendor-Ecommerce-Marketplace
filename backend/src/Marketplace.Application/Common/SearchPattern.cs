namespace Marketplace.Application.Common;

/// <summary>
/// The one definition of what a text search means across every endpoint that offers one.
/// </summary>
/// <remarks>
/// <para>
/// Searching has to behave identically wherever it happens: a shopper typing <c>AeroLux</c>, an
/// administrator typing <c>aerolux</c> and one typing <c>AEROLUX</c> are all looking for the same
/// listing, and a search that finds two of them is a search that cannot be trusted with a
/// support question.
/// </para>
/// <para>
/// PostgreSQL's <c>LIKE</c> is case-sensitive, so the comparison lowercases the column as well as
/// the term. The alternative — <c>ILIKE</c> — is the operator this really wants, but it arrives
/// with the Npgsql provider, and the Application layer is deliberately provider-agnostic (see its
/// project file: no Npgsql, nothing provider-specific). Nothing is given up by choosing this way:
/// every one of these searches wraps the term in <c>%…%</c>, and a leading wildcard already
/// forces a sequential scan, so there was no index for a sargable comparison to use. Should the
/// layering ever change, replacing <see cref="Contains"/> call sites with <c>ILIKE</c> is a
/// mechanical swap.
/// </para>
/// </remarks>
public static class SearchPattern
{
    /// <summary>
    /// A case-folded contains-pattern for <c>EF.Functions.Like</c>, or an empty string when there
    /// is nothing to search for.
    /// </summary>
    /// <remarks>
    /// The caller is expected to compare a lowercased column against this:
    /// <c>EF.Functions.Like(p.Name.ToLower(), SearchPattern.Contains(term))</c>. Returning the
    /// lowered term from one place is what stops the two halves drifting apart.
    /// </remarks>
    public static string Contains(string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return string.Empty;
        }

        return $"%{term.Trim().ToLowerInvariant()}%";
    }
}