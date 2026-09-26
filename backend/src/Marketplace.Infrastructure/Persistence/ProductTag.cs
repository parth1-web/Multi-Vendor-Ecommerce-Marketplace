using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Persistence;

/// <summary>Join entity linking products to tags.</summary>
public class ProductTag
{
    public Guid ProductId { get; set; }

    public Guid TagId { get; set; }

    public Product? Product { get; set; }

    public Tag? Tag { get; set; }
}

internal static class ModelBuilderExtensions
{
    /// <summary>
    /// Global query filters for soft-deleted catalogue rows. Without these a deleted
    /// product would still surface through any navigation include.
    /// </summary>
    public static void ApplyGlobalFilters(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>().HasQueryFilter(p => !p.IsDeleted);
        modelBuilder.Entity<Category>().HasQueryFilter(c => !c.IsDeleted);
    }
}
