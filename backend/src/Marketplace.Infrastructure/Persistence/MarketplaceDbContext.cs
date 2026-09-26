using Marketplace.Domain.Auditing;
using Marketplace.Domain.Cart;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Commissions;
using Marketplace.Domain.Coupons;
using Marketplace.Domain.Identity;
using Marketplace.Domain.Inventory;
using Marketplace.Domain.Notifications;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Refunds;
using Marketplace.Domain.Reviews;
using Marketplace.Domain.Sellers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Marketplace.Infrastructure.Persistence;

/// <summary>
/// The marketplace EF Core context. All mapping is explicit through
/// <c>Persistence/Configurations</c> — there are no conventions relied upon.
/// </summary>
public class MarketplaceDbContext(DbContextOptions<MarketplaceDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<UserAddress> UserAddresses => Set<UserAddress>();

    public DbSet<Seller> Sellers => Set<Seller>();
    public DbSet<SellerStore> SellerStores => Set<SellerStore>();
    public DbSet<SellerPayout> SellerPayouts => Set<SellerPayout>();

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductVariantOption> ProductVariantOptions => Set<ProductVariantOption>();
    public DbSet<ProductSpecification> ProductSpecifications => Set<ProductSpecification>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ProductTag> ProductTags => Set<ProductTag>();

    public DbSet<Inventory> Inventory => Set<Inventory>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();

    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Wishlist> Wishlists => Set<Wishlist>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<SellerOrder> SellerOrders => Set<SellerOrder>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();
    public DbSet<SellerOrderStatusHistory> SellerOrderStatusHistory => Set<SellerOrderStatusHistory>();

    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<PaymentWebhook> PaymentWebhooks => Set<PaymentWebhook>();

    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<RefundItem> RefundItems => Set<RefundItem>();

    public DbSet<Commission> Commissions => Set<Commission>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ReviewReply> ReviewReplies => Set<ReviewReply>();

    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CouponUsage> CouponUsages => Set<CouponUsage>();
    public DbSet<CouponProduct> CouponProducts => Set<CouponProduct>();

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>
    /// Provider name captured before the configurations run, so the concurrency-token
    /// configuration can adapt to the current provider.
    /// </summary>
    internal static string? CurrentProviderName { get; private set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        CurrentProviderName = Database.ProviderName;

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MarketplaceDbContext).Assembly);
        modelBuilder.ApplyGlobalFilters();
        DeclareApplicationGeneratedKeys(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// States that every primary key comes from the domain, never from the database.
    /// </summary>
    /// <remarks>
    /// EF assumes a Guid key is store-generated, so an entity it discovers through a tracked
    /// parent's collection looks like a row that is already stored: it is tracked as modified
    /// and the insert becomes an update that matches nothing. Saying the key is application
    /// generated is what lets change detection recognise those children as new, wherever in the
    /// graph they are added.
    /// </remarks>
    private static void DeclareApplicationGeneratedKeys(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.FindPrimaryKey() is not { } key)
            {
                continue;
            }

            foreach (var property in key.Properties)
            {
                property.ValueGenerated = ValueGenerated.Never;
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampConcurrencyTokens();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampConcurrencyTokens();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Gives every inserted or updated row a fresh concurrency token.
    /// </summary>
    /// <remarks>
    /// The token is a real column rather than a provider system column, so nothing fills it in
    /// on the way in. Leaving it to the database would mean an <c>UPDATE</c> never changes the
    /// value, the <c>WHERE RowVersion = @original</c> clause would then always match, and a
    /// lost update would go undetected. Stamping here keeps the guarantee identical on every
    /// provider: the value EF sends in the WHERE clause is the one it read, and the value it
    /// writes is always new.
    /// </remarks>
    private void StampConcurrencyTokens()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            if (entry.Metadata.FindProperty(ConcurrencyTokenConfiguration.RowVersionPropertyName) is not { } property)
            {
                continue;
            }

            if (property.ClrType != typeof(byte[]))
            {
                continue;
            }

            entry.CurrentValues[property.Name] = ConcurrencyTokenConfiguration.NewToken();
        }
    }
}
