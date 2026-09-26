using Marketplace.Domain.Cart;
using Marketplace.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.Infrastructure.Persistence.Configurations;

public sealed class InventoryConfiguration : IEntityTypeConfiguration<Inventory>
{
    public void Configure(EntityTypeBuilder<Inventory> builder)
    {
        builder.ToTable("inventory");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.AvailableQuantity).IsRequired();
        builder.Property(i => i.ReservedQuantity).IsRequired();
        builder.Property(i => i.SoldQuantity).IsRequired();
        builder.Property(i => i.LowStockThreshold).IsRequired();
        builder.ConfigureRowVersion();

        builder.HasIndex(i => i.ProductVariantId).IsUnique().HasDatabaseName("ux_inventory_variant");
        builder.HasIndex(i => new { i.SellerId, i.AvailableQuantity }).HasDatabaseName("ix_inventory_seller");

        builder.Ignore(i => i.SellableQuantity);
        builder.Ignore(i => i.IsOutOfStock);
        builder.Ignore(i => i.IsLowStock);

        builder.HasOne<Domain.Catalog.ProductVariant>()
            .WithOne()
            .HasForeignKey<Inventory>(i => i.ProductVariantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> builder)
    {
        builder.ToTable("inventory_transactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(48).IsRequired();
        builder.Property(t => t.ReferenceType).HasColumnType("varchar(64)");
        builder.Property(t => t.Reason).HasColumnType("varchar(300)");

        builder.HasIndex(t => new { t.ProductVariantId, t.CreatedAt }).HasDatabaseName("ix_inventory_tx_variant");
        builder.HasIndex(t => new { t.SellerId, t.CreatedAt }).HasDatabaseName("ix_inventory_tx_seller");
        builder.HasIndex(t => new { t.ReferenceType, t.ReferenceId }).HasDatabaseName("ix_inventory_tx_reference");
    }
}

public sealed class InventoryReservationConfiguration : IEntityTypeConfiguration<InventoryReservation>
{
    public void Configure(EntityTypeBuilder<InventoryReservation> builder)
    {
        builder.ToTable("inventory_reservations");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ReleaseReason).HasColumnType("varchar(100)");

        builder.HasIndex(r => r.InventoryId).HasDatabaseName("ix_inventory_reservations_inventory");
        builder.HasIndex(r => r.OrderId).HasDatabaseName("ix_inventory_reservations_order");
        builder.HasIndex(r => new { r.ReleasedAt, r.ExpiresAt }).HasDatabaseName("ix_inventory_reservations_expiry");

        builder.Ignore(r => r.IsReleased);
    }
}

public sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("carts");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.OwnerType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(c => c.GuestToken).HasColumnType("varchar(128)");

        builder.HasIndex(c => c.UserId).IsUnique().HasDatabaseName("ux_carts_user");
        builder.HasIndex(c => c.GuestToken).IsUnique().HasDatabaseName("ux_carts_guest");
        builder.HasIndex(c => c.LastActivityAt).HasDatabaseName("ix_carts_activity");


        builder.Ignore(c => c.Items);

        builder.HasMany(c => c.Items)
            .WithOne(i => i.Cart!)
            .HasForeignKey(i => i.CartId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("cart_items");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.UnitPrice).HasColumnType("numeric(18,2)").IsRequired();

        builder.HasIndex(i => new { i.CartId, i.ProductVariantId }).IsUnique().HasDatabaseName("ux_cart_items_variant");

        builder.Ignore(i => i.LineTotal);

        builder.HasOne<Domain.Catalog.Product>()
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class WishlistConfiguration : IEntityTypeConfiguration<Wishlist>
{
    public void Configure(EntityTypeBuilder<Wishlist> builder)
    {
        builder.ToTable("wishlists");
        builder.HasKey(w => w.Id);

        builder.HasIndex(w => w.UserId).IsUnique().HasDatabaseName("ux_wishlists_user");

        builder.Ignore(w => w.Items);

        builder.HasMany(w => w.Items)
            .WithOne(i => i.Wishlist!)
            .HasForeignKey(i => i.WishlistId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(w => w.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class WishlistItemConfiguration : IEntityTypeConfiguration<WishlistItem>
{
    public void Configure(EntityTypeBuilder<WishlistItem> builder)
    {
        builder.ToTable("wishlist_items");
        builder.HasKey(i => i.Id);

        builder.HasIndex(i => new { i.WishlistId, i.ProductId }).IsUnique().HasDatabaseName("ux_wishlist_items_product");
    }
}
