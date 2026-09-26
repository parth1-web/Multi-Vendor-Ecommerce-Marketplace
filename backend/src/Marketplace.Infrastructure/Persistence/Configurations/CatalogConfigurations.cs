using Marketplace.Domain.Catalog;
using Marketplace.Domain.Sellers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.Infrastructure.Persistence.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasColumnType("varchar(120)").IsRequired();
        builder.Property(c => c.SlugValue).HasColumnType("varchar(160)").IsRequired();
        builder.Property(c => c.Description).HasColumnType("varchar(2000)");
        builder.Property(c => c.ImageUrl).HasColumnType("varchar(512)");

        builder.HasIndex(c => c.SlugValue).IsUnique().HasDatabaseName("ux_categories_slug");
        builder.HasIndex(c => new { c.ParentId, c.DisplayOrder }).HasDatabaseName("ix_categories_parent_order");
        builder.HasIndex(c => c.IsActive).HasDatabaseName("ix_categories_active");

        builder.Ignore(c => c.Slug);
        builder.Ignore(c => c.IsRoot);

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).HasColumnType("varchar(200)").IsRequired();
        builder.Property(p => p.SlugValue).HasColumnType("varchar(160)").IsRequired();
        builder.Property(p => p.ShortDescription).HasColumnType("varchar(500)").IsRequired();
        builder.Property(p => p.Description).HasColumnType("text").IsRequired();
        builder.Property(p => p.BasePrice).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(p => p.CompareAtPrice).HasColumnType("numeric(18,2)");
        builder.Property(p => p.Brand).HasColumnType("varchar(120)");
        builder.Property(p => p.Model).HasColumnType("varchar(120)");
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(p => p.RejectionReason).HasConversion<string>().HasMaxLength(48);
        builder.Property(p => p.RejectionNote).HasColumnType("varchar(1000)");
        builder.Property(p => p.RatingAverage).HasColumnType("numeric(3,2)").IsRequired();
        builder.ConfigureRowVersion();

        builder.HasIndex(p => p.SlugValue).IsUnique().HasDatabaseName("ux_products_slug");
        builder.HasIndex(p => new { p.SellerId, p.Status, p.CreatedAt }).HasDatabaseName("ix_products_seller_status");
        builder.HasIndex(p => new { p.CategoryId, p.Status, p.CreatedAt }).HasDatabaseName("ix_products_category_status");
        builder.HasIndex(p => p.BasePrice).HasDatabaseName("ix_products_price");
        builder.HasIndex(p => p.IsFeatured).HasDatabaseName("ix_products_featured");
        builder.HasIndex(p => p.RatingAverage).HasDatabaseName("ix_products_rating");

        builder.Ignore(p => p.Slug);
        builder.Ignore(p => p.DiscountPercentage);
        builder.Ignore(p => p.IsPubliclyVisible);

        // The domain exposes read-only collections backed by private lists; EF materialises
        // straight into the backing field so no setter is ever needed.
        builder.HasMany(p => p.Images)
            .WithOne(i => i.Product!)
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Images).HasField("_images").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Variants)
            .WithOne(v => v.Product!)
            .HasForeignKey(v => v.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Variants).HasField("_variants").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Specifications)
            .WithOne(s => s.Product!)
            .HasForeignKey(s => s.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Specifications).HasField("_specifications").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Tags).WithMany(t => t.Products);
        builder.Navigation(p => p.Tags).HasField("_tags").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Seller>()
            .WithMany()
            .HasForeignKey(p => p.SellerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("product_images");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Url).HasColumnType("varchar(512)").IsRequired();
        builder.Property(i => i.AltText).HasColumnType("varchar(300)");

        builder.HasIndex(i => new { i.ProductId, i.SortOrder }).HasDatabaseName("ix_product_images_product_order");
    }
}

public sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("product_variants");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Sku).HasColumnType("varchar(64)").IsRequired();
        builder.Property(v => v.Name).HasColumnType("varchar(200)").IsRequired();
        builder.Property(v => v.Price).HasColumnType("numeric(18,2)").IsRequired();

        builder.HasIndex(v => v.Sku).IsUnique().HasDatabaseName("ux_product_variants_sku");
        builder.HasIndex(v => new { v.ProductId, v.SortOrder }).HasDatabaseName("ix_product_variants_product_order");

        builder.HasMany(v => v.Options)
            .WithOne(o => o.Variant!)
            .HasForeignKey(o => o.VariantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ProductVariantOptionConfiguration : IEntityTypeConfiguration<ProductVariantOption>
{
    public void Configure(EntityTypeBuilder<ProductVariantOption> builder)
    {
        builder.ToTable("product_variant_options");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Name).HasColumnType("varchar(64)").IsRequired();
        builder.Property(o => o.Value).HasColumnType("varchar(64)").IsRequired();

        builder.HasIndex(o => new { o.VariantId, o.Name, o.Value }).HasDatabaseName("ux_variant_options");
    }
}

public sealed class ProductSpecificationConfiguration : IEntityTypeConfiguration<ProductSpecification>
{
    public void Configure(EntityTypeBuilder<ProductSpecification> builder)
    {
        builder.ToTable("product_specifications");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Key).HasColumnType("varchar(80)").IsRequired();
        builder.Property(s => s.Value).HasColumnType("varchar(500)").IsRequired();

        builder.HasIndex(s => new { s.ProductId, s.SortOrder }).HasDatabaseName("ix_product_specs_product_order");
    }
}

public sealed class ProductTagConfiguration : IEntityTypeConfiguration<ProductTag>
{
    public void Configure(EntityTypeBuilder<ProductTag> builder)
    {
        builder.ToTable("product_tags");
        builder.HasKey(pt => new { pt.ProductId, pt.TagId });
    }
}

public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("tags");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasColumnType("varchar(80)").IsRequired();
        builder.HasIndex(t => t.Name).IsUnique().HasDatabaseName("ux_tags_name");
    }
}
