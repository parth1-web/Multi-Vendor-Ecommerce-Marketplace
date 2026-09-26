using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Modules.Auth.Services;
using Marketplace.Domain.Cart;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Marketplace.Domain.Coupons;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Identity;
using Marketplace.Domain.Inventory;
using Marketplace.Domain.Sellers;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using InventoryRecord = Marketplace.Domain.Inventory.Inventory;

namespace Marketplace.IntegrationTests.Infrastructure;

/// <summary>
/// Deterministic world used by the integration suite: three sellers (active, pending,
/// suspended), two customers, an admin, a category tree, products with stock and coupons.
/// </summary>
public sealed class MarketplaceTestData(IServiceProvider rootServices, FixedClock clock)
{
    private IServiceScope? _scope;

    private IServiceScope Scope => _scope ??= rootServices.CreateScope();

    private MarketplaceDbContext Context => Scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
    public const string AdminEmail = "admin@test.dev";
    public const string AdminPassword = "Admin@123";
    public const string SellerEmail = "seller-a@test.dev";
    public const string SellerPassword = "Seller@123";
    public const string SecondSellerEmail = "seller-b@test.dev";
    public const string PendingSellerEmail = "seller-pending@test.dev";
    public const string SuspendedSellerEmail = "seller-suspended@test.dev";
    public const string CustomerEmail = "customer@test.dev";
    public const string SecondCustomerEmail = "customer-2@test.dev";
    public const string CustomerPassword = "Customer@123";

    public Guid AdminId { get; private set; }

    public Guid SellerAId { get; private set; }

    public Guid SellerBId { get; private set; }

    public Guid PendingSellerId { get; private set; }

    public Guid SuspendedSellerId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid SecondCustomerId { get; private set; }

    public Guid AddressId { get; private set; }

    public Guid CategoryId { get; private set; }

    public Guid ChildCategoryId { get; private set; }

    public Guid SellerAProductId { get; private set; }

    public string SellerAProductSlug { get; private set; } = string.Empty;

    public Guid SellerAProductVariantId { get; private set; }

    public Guid SellerBProductId { get; private set; }

    public Guid SellerBProductVariantId { get; private set; }

    /// <summary>A product filed under the child category, for the subcategory listing filter.</summary>
    public Guid ChildCategoryProductId { get; private set; }

    /// <summary>The single-unit product, used for the oversell scenario.</summary>
    public Guid SingleUnitProductId { get; private set; }

    public Guid SingleUnitVariantId { get; private set; }

    public string SellerAStoreSlug { get; private set; } = string.Empty;

    public async Task InitialiseAsync()
    {
        var hasher = rootServices.GetRequiredService<IPasswordHasher>();
        var now = clock.UtcNow;

        // The schema and the seed data are shared by every test in the class, so the seed
        // must be idempotent: a second call verifies the world instead of duplicating it.
        var alreadySeeded = await Context.Users.AnyAsync(u => u.Email == CustomerEmail).ConfigureAwait(false);
        if (alreadySeeded)
        {
            await ResolveExistingAsync().ConfigureAwait(false);
            return;
        }

        AdminId = (await CreateUserAsync(AdminEmail, AdminPassword, "Super", "Admin", UserRole.SuperAdmin, hasher, now)).Id;
        var customer = await CreateUserAsync(CustomerEmail, CustomerPassword, "Aarav", "Sharma", UserRole.Customer, hasher, now);
        CustomerId = customer.Id;
        SecondCustomerId = (await CreateUserAsync(SecondCustomerEmail, CustomerPassword, "Sita", "Thapa", UserRole.Customer, hasher, now)).Id;

        var address = UserAddress.Create(customer.Id, "Home", customer.FullName, "+9779800000000",
            "12 Ratna Marg", null, "Kathmandu", "Bagmati", "44600", "NP", true, now);

        AddressId = address.Id;
        Context.UserAddresses.Add(address);
        await Context.SaveChangesAsync();

        (SellerAId, SellerAStoreSlug) = await CreateSellerAsync(SellerEmail, SellerPassword, "Tech World", "tech-world", SellerStatus.Active, 10m, hasher, now);
        (SellerBId, _) = await CreateSellerAsync(SecondSellerEmail, SellerPassword, "Fashion Hub", "fashion-hub", SellerStatus.Active, 12m, hasher, now);
        (PendingSellerId, _) = await CreateSellerAsync(PendingSellerEmail, SellerPassword, "Pending Store", "pending-store", SellerStatus.Pending, 10m, hasher, now);
        (SuspendedSellerId, _) = await CreateSellerAsync(SuspendedSellerEmail, SellerPassword, "Suspended Store", "suspended-store", SellerStatus.Suspended, 10m, hasher, now);

        var root = Category.Create("Electronics", Slug.Create("electronics"), "Root category", null, 0, now);
        Context.Categories.Add(root);
        await Context.SaveChangesAsync();

        var child = Category.Create("Headphones", Slug.Create("headphones"), "Child category", root.Id, 0, now);
        Context.Categories.Add(child);
        await Context.SaveChangesAsync();

        CategoryId = root.Id;
        ChildCategoryId = child.Id;

        // Seller A product with healthy stock.
        var productA = CreateProduct(SellerAId, "AeroLux Headphones", "aerolux-headphones", 249m, 329m, 25, now, out var variantA);
        SellerAProductId = productA.Id;
        SellerAProductSlug = productA.SlugValue;
        SellerAProductVariantId = variantA.Id;

        // Seller B product, so a cart can span two sellers.
        var productB = CreateProduct(SellerBId, "Merino Crew", "merino-crew", 89m, null, 40, now, out var variantB);
        SellerBProductId = productB.Id;
        SellerBProductVariantId = variantB.Id;

        // A single-unit variant used for the concurrency test.
        var productC = CreateProduct(SellerAId, "Last Unit", "last-unit", 499m, null, 1, now, out var variantC);
        SingleUnitProductId = productC.Id;
        SingleUnitVariantId = variantC.Id;

        // Seller B's product filed under the child category, so a parent category listing has
        // something beneath it to include.
        ChildCategoryProductId = CreateProduct(SellerBId, "Earbuds Lite", "earbuds-lite", 59m, null, 12, now, out _, ChildCategoryId).Id;

        var coupon = Coupon.Create(null, CouponScope.Global, "TEST10", "10% off", CouponDiscountType.Percentage, 10m, 50m, 25m, 100, 5,
            now.AddDays(-1), now.AddDays(30), now);
        Context.Coupons.Add(coupon);

        await Context.SaveChangesAsync();
    }

    /// <summary>Re-points the exposed ids at the rows created by an earlier seeding pass.</summary>
    private async Task ResolveExistingAsync()
    {
        AdminId = (await Context.Users.FirstAsync(u => u.Email == AdminEmail)).Id;
        var customer = await Context.Users.FirstAsync(u => u.Email == CustomerEmail);
        CustomerId = customer.Id;
        SecondCustomerId = (await Context.Users.FirstAsync(u => u.Email == SecondCustomerEmail)).Id;
        AddressId = (await Context.UserAddresses.FirstAsync(a => a.UserId == CustomerId)).Id;

        SellerAId = (await Context.Sellers.FirstAsync(s => s.BusinessName == "Tech World")).Id;
        SellerBId = (await Context.Sellers.FirstAsync(s => s.BusinessName == "Fashion Hub")).Id;
        PendingSellerId = (await Context.Sellers.FirstAsync(s => s.BusinessName == "Pending Store")).Id;
        SuspendedSellerId = (await Context.Sellers.FirstAsync(s => s.BusinessName == "Suspended Store")).Id;
        SellerAStoreSlug = (await Context.SellerStores.FirstAsync(s => s.SellerId == SellerAId)).SlugValue;

        CategoryId = (await Context.Categories.FirstAsync(c => c.SlugValue == "electronics")).Id;
        ChildCategoryId = (await Context.Categories.FirstAsync(c => c.SlugValue == "headphones")).Id;

        var productA = await Context.Products.FirstAsync(p => p.SlugValue == "aerolux-headphones");
        SellerAProductId = productA.Id;
        SellerAProductSlug = productA.SlugValue;
        SellerAProductVariantId = (await Context.ProductVariants.FirstAsync(v => v.ProductId == productA.Id)).Id;

        var productB = await Context.Products.FirstAsync(p => p.SlugValue == "merino-crew");
        SellerBProductId = productB.Id;
        SellerBProductVariantId = (await Context.ProductVariants.FirstAsync(v => v.ProductId == productB.Id)).Id;

        var productC = await Context.Products.FirstAsync(p => p.SlugValue == "last-unit");
        SingleUnitProductId = productC.Id;
        SingleUnitVariantId = (await Context.ProductVariants.FirstAsync(v => v.ProductId == productC.Id)).Id;

        ChildCategoryProductId = (await Context.Products.FirstAsync(p => p.SlugValue == "earbuds-lite")).Id;
    }

    private async Task<User> CreateUserAsync(string email, string password, string first, string last, UserRole role, IPasswordHasher hasher, DateTimeOffset now)
    {
        var user = User.CreateAs(email, hasher.Hash(password), first, last, role, null, now);
        Context.Users.Add(user);
        await Context.SaveChangesAsync();
        return user;
    }

    private async Task<(Guid SellerId, string Slug)> CreateSellerAsync(
        string email, string password, string storeName, string slug, SellerStatus status, decimal rate, IPasswordHasher hasher, DateTimeOffset now)
    {
        var user = await CreateUserAsync(email, password, "Store", "Owner", UserRole.Seller, hasher, now);

        var seller = Seller.Apply(user.Id, storeName, storeName, "+9779800000000", "Kathmandu", null, null, null, rate, now);

        if (status == SellerStatus.Active)
        {
            seller.Approve(rate, now);
        }
        else if (status == SellerStatus.Suspended)
        {
            seller.Approve(rate, now);
            seller.Suspend("Suspended for testing", now);
        }

        var store = SellerStore.Create(seller.Id, storeName, Slug.Create(slug), $"{storeName} description", now);
        store.SetActive(true, now);

        Context.Sellers.Add(seller);
        await Context.SaveChangesAsync();

        Context.SellerStores.Add(store);
        await Context.SaveChangesAsync();

        _ = user;
        return (seller.Id, store.SlugValue);
    }

    private Product CreateProduct(Guid sellerId, string name, string slug, decimal price, decimal? compareAt, int stock, DateTimeOffset now, out ProductVariant variant, Guid? categoryId = null)
    {
        var product = Product.Create(sellerId, categoryId ?? CategoryId, name, Slug.Create(slug), $"{name} short", $"{name} long", price, compareAt, "TestBrand", null, now);
        product.AddImage($"https://cdn.test/{slug}.jpg", name, true, now);

        variant = product.AddVariant($"SKU-{slug.ToUpperInvariant()}", "Default", null, 0, now);

        Context.Products.Add(product);
        Context.Inventory.Add(InventoryRecord.Create(variant.Id, product.Id, sellerId, stock, 3, now));

        product.SubmitForApproval(now);
        product.Approve(now);

        return product;
    }
}