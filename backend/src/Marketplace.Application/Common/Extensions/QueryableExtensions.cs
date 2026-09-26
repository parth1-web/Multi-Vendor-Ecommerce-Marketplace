using Marketplace.Application.Common.Models;
using Marketplace.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Common.Extensions;

/// <summary>Shared query helpers used by every list endpoint.</summary>
public static class QueryableExtensions
{
    /// <summary>Applies server-side pagination and returns the envelope.</summary>
    public static async Task<PagedResult<TResult>> ToPagedResultAsync<TSource, TResult>(
        this IQueryable<TSource> query,
        PageRequest page,
        Func<TSource, TResult> selector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        if (total == 0)
        {
            return PagedResult<TResult>.Empty(page.Page, page.PageSize);
        }

        var items = await query
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(s => selector(s))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<TResult>(items, page.Page, page.PageSize, total);
    }

    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        if (total == 0)
        {
            return PagedResult<T>.Empty(page.Page, page.PageSize);
        }

        var items = await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult<T>(items, page.Page, page.PageSize, total);
    }
}
