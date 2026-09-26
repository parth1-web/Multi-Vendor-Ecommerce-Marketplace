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

        // Owned/dependent rows inherit the parent's soft-delete filter so a deleted product
        // can never surface through a child navigation include.
        modelBuilder.Entity<ProductImage>().HasQueryFilter(i => !i.Product!.IsDeleted);
        modelBuilder.Entity<ProductVariant>().HasQueryFilter(v => !v.Product!.IsDeleted);
        modelBuilder.Entity<ProductSpecification>().HasQueryFilter(s => !s.Product!.IsDeleted);
        modelBuilder.Entity<ProductTag>().HasQueryFilter(pt => !pt.Product!.IsDeleted);
        modelBuilder.Entity<CouponProduct>().HasQueryFilter(cp => !cp.Product!.IsDeleted);
        modelBuilder.Entity<ProductVariantOption>().HasQueryFilter(o => !o.Variant!.Product!.IsDeleted);
    }
}
