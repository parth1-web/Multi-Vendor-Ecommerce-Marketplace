using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Modules.Auth.Abstractions;
using Marketplace.Application.Modules.Auth.Services;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Marketplace.Domain.Coupons;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Identity;
using Marketplace.Domain.Sellers;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using InventoryRecord = Marketplace.Domain.Inventory.Inventory;

namespace Marketplace.Infrastructure.Persistence.Seeding;

/// <summary>
/// Development/demo seed. Idempotent: re-running it never duplicates data, so it is safe
/// to call on every startup. Never invoked in Production.
/// </summary>
public sealed class DatabaseSeeder(
    MarketplaceDbContext context,
    IPasswordHasher passwordHasher,
    IClock clock,
    IOptions<MarketplaceOptions> options,
    ILogger<DatabaseSeeder> logger)
{
    public const string AdminEmail = "admin@marketplace.dev";
    public const string ManagerEmail = "manager@marketplace.dev";
    public const string SellerEmail = "seller@marketplace.dev";
    public const string FashionSellerEmail = "fashion@marketplace.dev";
    public const string CustomerEmail = "customer@marketplace.dev";
    public const string DefaultPassword = "Admin@123";

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var commissionRate = options.Value.CommissionRate;

        var admin = await EnsureUserAsync(AdminEmail, "Super", "Admin", UserRole.SuperAdmin, "Super@123", now, cancellationToken).ConfigureAwait(false);
        _ = await EnsureUserAsync(ManagerEmail, "Ops", "Manager", UserRole.Admin, DefaultPassword, now, cancellationToken).ConfigureAwait(false);
        var customer = await EnsureUserAsync(CustomerEmail, "Aarav", "Sharma", UserRole.Customer, "Customer@123", now, cancellationToken).ConfigureAwait(false);

        var techSeller = await EnsureSellerAsync(SellerEmail, "Roshan", "Karki", "TechWorld", "tech-world",
            "Audio, wearables and accessories curated by engineers.", 10m, commissionRate, now, cancellationToken).ConfigureAwait(false);

        var fashionSeller = await EnsureSellerAsync(FashionSellerEmail, "Sita", "Thapa", "FashionHub", "fashion-hub",
            "Everyday wear, denim and accessories from independent labels.", 12m, commissionRate, now, cancellationToken).ConfigureAwait(false);

        var categories = await EnsureCategoriesAsync(now, cancellationToken).ConfigureAwait(false);

        // Per product rather than "if the table is empty". A catalogue that is skipped because
        // the table is not empty is skipped because a developer added a product of their own, and
        // a catalogue that is only partly there is never healed.
        await EnsureProductsAsync(techSeller, fashionSeller, categories, now, cancellationToken).ConfigureAwait(false);

        await SyncProductCountsAsync(now, cancellationToken).ConfigureAwait(false);

        await EnsureCouponsAsync(now, cancellationToken).ConfigureAwait(false);
        await EnsureAddressAsync(customer, now, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Seed data verified.");
    }

    /// <summary>
    /// Brings the denormalised product counts back in line with the products that are on sale.
    /// </summary>
    /// <remarks>
    /// The catalogue service keeps these in step whenever a listing is approved, moved or removed.
    /// The seeder writes products straight to the database rather than through that service, so
    /// without this the demo storefronts sit at zero while showing five products each, and every
    /// category reads "0 products". A count that is wrong in the demo data is a count nobody
    /// trusts later, and it is also the guard that stops a non-empty category being deleted.
    /// </remarks>
    private async Task SyncProductCountsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var stores = await context.SellerStores.ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var store in stores)
        {
            var published = await context.Products.CountAsync(
                p => p.SellerId == store.SellerId && p.Status == ProductStatus.Published,
                cancellationToken).ConfigureAwait(false);

            if (store.ProductCount != published)
            {
                store.UpdateProductCount(published, now);
            }
        }

        foreach (var category in await context.Categories.ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            var published = await context.Products.CountAsync(
                p => p.CategoryId == category.Id && p.Status == ProductStatus.Published,
                cancellationToken).ConfigureAwait(false);

            if (category.ProductCount != published)
            {
                category.RecalculateProductCount(published, now);
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }


    private async Task<User> EnsureUserAsync(string email, string firstName, string lastName, UserRole role, string password, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await context.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var user = User.CreateAs(email, passwordHasher.Hash(password), firstName, lastName, role, null, now);
        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return user;
    }

    private async Task<Seller> EnsureSellerAsync(
        string email, string firstName, string lastName, string storeName, string storeSlug, string description,
        decimal rate, decimal commissionRate, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var user = await EnsureUserAsync(email, firstName, lastName, UserRole.Seller, "Seller@123", now, cancellationToken).ConfigureAwait(false);

        var seller = await context.Sellers.FirstOrDefaultAsync(s => s.UserId == user.Id, cancellationToken).ConfigureAwait(false);

        if (seller is null)
        {
            seller = Seller.Apply(user.Id, $"{firstName} {lastName}", storeName, "+9779800000000", "Kathmandu, Nepal",
                null, null, null, rate, now);
            seller.Approve(commissionRate, now);

            var store = SellerStore.Create(seller.Id, storeName, Slug.Create(storeSlug), description, now);
            store.UpdateProfile(storeName, description, null, null, "support@marketplace.dev", "+9779800000001",
                "30-day returns on unused items in original packaging.",
                "Standard delivery 2-4 business days.", 2019, now);

            context.Sellers.Add(seller);
            context.SellerStores.Add(store);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return seller;
        }

        var existingStore = await context.SellerStores.FirstOrDefaultAsync(s => s.SellerId == seller.Id, cancellationToken).ConfigureAwait(false);
        _ = existingStore;
        return seller;
    }

    private async Task<IReadOnlyDictionary<string, Category>> EnsureCategoriesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var definitions = new (string Key, string Name, string Slug, string? ParentKey, string Description)[]
        {
            ("electronics", "Electronics", "electronics", null, "Phones, laptops, audio and smart home."),
            ("audio", "Headphones & Audio", "headphones-audio", "electronics", "Headphones, speakers and microphones."),
            ("wearables", "Wearables", "wearables", "electronics", "Smartwatches, bands and trackers."),
            ("mobile", "Mobile Phones", "mobile-phones", "electronics", "Smartphones from global and local brands."),
            ("fashion", "Fashion", "fashion", null, "Clothing, footwear and accessories."),
            ("mens", "Men's Clothing", "mens-clothing", "fashion", "Shirts, denim, jackets and more."),
            ("womens", "Women's Clothing", "womens-clothing", "fashion", "Dresses, tops, denim and more."),
            ("accessories", "Accessories", "accessories", "fashion", "Bags, wallets, belts and jewellery."),
            ("home", "Home & Living", "home-living", null, "Kitchen, decor and everyday essentials."),
            ("kitchen", "Kitchen", "kitchen", "home", "Cookware, appliances and storage.")

        };

        var result = new Dictionary<string, Category>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            var category = await context.Categories.FirstOrDefaultAsync(c => c.SlugValue == definition.Slug, cancellationToken).ConfigureAwait(false);
            if (category is not null)
            {
                result[definition.Key] = category;
                continue;
            }

            Guid? parentId = definition.ParentKey is not null && result.TryGetValue(definition.ParentKey, out var parent) ? parent.Id : null;
            category = Category.Create(definition.Name, Slug.Create(definition.Slug), definition.Description, parentId, 0, now);
            context.Categories.Add(category);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            result[definition.Key] = category;
        }

        return result;
    }

    private async Task EnsureProductsAsync(
        Seller techSeller,
        Seller fashionSeller,
        IReadOnlyDictionary<string, Category> categories,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // One row per slug, the same rule the categories use. Deleting one product from the demo
        // catalogue and restarting gets that product back, and the other eleven are left alone
        // instead of the whole insert failing on a unique index.
        var alreadySeeded = await context.Products
            .Select(p => p.SlugValue)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var catalogue = new (string Name, string Slug, string Short, string Description, decimal Price, decimal? CompareAt, string Brand, string CategoryKey, string SellerSku, string[] Specs, string SellerSlot)[]

        {
            ("AeroLux Wireless Headphones", "aerolux-wireless-headphones", "Over-ear ANC headphones with 40-hour battery.",
                "AeroLux combines adaptive noise cancelling, 40 hours of playback and a memory-foam headband tuned for long-haul travel. Multipoint pairing keeps a laptop and phone connected at once.",
                249.00m, 329.00m, "AeroLux", "audio", "AERO-ANC-001",
                ["Battery life: 40 hours", "Driver: 40 mm", "Bluetooth: 5.3", "Weight: 268 g"], "tech"),

            ("Titan Elite Smartwatch", "titan-elite-smartwatch", "Fitness tracking with a 12-day battery.",
                "Titan Elite tracks heart rate, SpO2 and sleep with a 1.4 inch AMOLED display, 5 ATM water resistance and 12 days of typical battery life.",
                349.99m, 429.00m, "TechTime", "wearables", "TITAN-EL-002",
                ["Display: 1.4 in AMOLED", "Battery: 12 days", "Water resistance: 5 ATM", "Sensors: HR, SpO2, GPS"], "tech"),

            ("Flagship Pro 5G Smartphone", "flagship-pro-5g-smartphone", "6.7 inch display, 50 MP camera, 5000 mAh battery.",
                "The Flagship Pro pairs a 120 Hz LTPO display with a triple 50 MP camera system, 120 W charging and an IP68 rating.",
                899.00m, null, "Orbis", "mobile", "ORB-FP-5G-003",
                ["Display: 6.7 in LTPO 120 Hz", "Camera: 50 MP triple", "Battery: 5000 mAh", "Charging: 120 W"], "tech"),

            ("Nimbus 14 Ultrabook", "nimbus-14-ultrabook", "1.2 kg magnesium chassis, 18-hour battery.",
                "A 14 inch ultrabook with a 2.8 K 120 Hz display, 32 GB of memory and a fanless design that stays silent under load.",
                1299.00m, 1499.00m, "Nimbus", "electronics", "NIM-U14-004",
                ["Display: 14 in 2.8 K", "Memory: 32 GB", "Storage: 1 TB NVMe", "Weight: 1.2 kg"], "tech"),

            ("StudioMic USB Condenser", "studiomic-usb-condenser", "Cardioid condenser microphone for streaming.",
                "A USB-C condenser microphone with a built-in shock mount, zero-latency monitoring and a cardioid pattern that isolates your voice.",
                129.50m, 159.00m, "AeroLux", "audio", "AERO-MIC-005",
                ["Pattern: cardioid", "Connection: USB-C", "Sample rate: 24 bit / 192 kHz"], "tech"),

            ("Everyday Merino Crew", "everyday-merino-crew", "Mid-weight 100% merino wool crew neck.",
                "A year-round crew neck knitted from 100% responsibly sourced merino wool. Temperature regulating, odour resistant and machine washable.",
                89.00m, null, "Northloom", "mens", "NL-MRC-101",
                ["Material: 100% merino wool", "Fit: regular", "Care: machine wash cold"], "fashion"),

            ("Selvedge Straight Jeans", "selvedge-straight-jeans", "13.5 oz Japanese selvedge denim.",
                "Cut from 13.5 oz selvedge denim woven on shuttle looms in Okayama, with a straight leg and a mid rise that sits well with boots or sneakers.",
                145.00m, 189.00m, "Indigo Foundry", "mens", "IF-SJD-102",
                ["Weight: 13.5 oz", "Origin: Okayama, Japan", "Fit: straight", "Closure: button fly"], "fashion"),

            ("Linen Blend Summer Dress", "linen-blend-summer-dress", "Breathable linen-viscose midi dress.",
                "A relaxed midi dress in a linen-viscose blend with side pockets and a drawstring waist — built for hot days and long evenings.",
                119.00m, null, "Northloom", "womens", "NL-DRS-103",
                ["Composition: 55% linen, 45% viscose", "Length: midi", "Features: side pockets"], "fashion"),

            ("Everyday Leather Tote", "everyday-leather-tote", "Full-grain leather tote with a padded sleeve.",
                "A structured tote in full-grain leather with a padded 13-inch laptop sleeve, an interior zip pocket and solid brass hardware.",
                229.00m, 279.00m, "Craft & Hide", "accessories", "CH-TOT-104",
                ["Material: full-grain leather", "Laptop sleeve: 13 in", "Hardware: solid brass"], "fashion"),

            ("Cast Iron Skillet 26cm", "cast-iron-skillet-26cm", "Pre-seasoned cast iron, oven safe to 260°C.",
                "A classic 26 cm cast iron skillet, pre-seasoned and ready to use. Ovensafe to 260 °C and improves with every cook.",
                59.00m, 74.00m, "Hearthstone", "kitchen", "HS-CSK-201",
                ["Diameter: 26 cm", "Material: cast iron", "Oven safe: 260 °C"], "fashion"),

            ("Stackable Glass Storage Set", "stackable-glass-storage-set", "Six borosilicate containers with bamboo lids.",
                "Six nesting borosilicate glass containers with bamboo lids and silicone seals. Microwave, dishwasher and freezer safe.",
                49.00m, null, "Hearthstone", "kitchen", "HS-GST-202",
                ["Pieces: 6", "Material: borosilicate glass", "Lids: bamboo + silicone"], "fashion"),

            ("Linen Duvet Cover Set", "linen-duvet-cover-set", "Stonewashed linen duvet cover and two pillowcases.",
                "A stonewashed linen duvet cover with two matching pillowcases. Breathable, temperature regulating and softer with every wash.",
                199.00m, 249.00m, "Northloom", "home", "NL-DUV-203",

                ["Composition: 100% linen", "Includes: duvet cover + 2 pillowcases", "Closure: button"], "fashion")
        };

        // A tag name is unique across the whole catalogue, so the second product by the same
        // brand would collide with the first. The rows are resolved once and shared, which is
        // what the join expects: one tag, many products.
        var tags = await ResolveTagsAsync(catalogue.Select(item => item.Brand ?? "General"), now, cancellationToken)
            .ConfigureAwait(false);

        foreach (var item in catalogue)
        {
            if (alreadySeeded.Contains(item.Slug))
            {
                continue;
            }

            var seller = item.SellerSlot == "tech" ? techSeller : fashionSeller;

            // The catalogue names a category by its key in the table above. A key that does not
            // exist is a mistake in this file, and saying so plainly beats a KeyNotFoundException
            // that reads as though the database were empty.
            if (!categories.TryGetValue(item.CategoryKey, out var category))
            {
                throw new InvalidOperationException(
                    $"Seed product '{item.Slug}' refers to category key '{item.CategoryKey}', which is not one of: {string.Join(", ", categories.Keys)}.");
            }



            var product = Product.Create(
                seller.Id,
                category.Id,
                item.Name,
                Slug.Create(item.Slug),
                item.Short,
                item.Description,
                item.Price,
                item.CompareAt,
                item.Brand,
                null,
                now);

            product.AddImage($"https://picsum.photos/seed/{item.Slug}/800/800", item.Name, true, now);
            product.AddImage($"https://picsum.photos/seed/{item.Slug}-alt/800/800", $"{item.Name} alternate view", false, now);

            var variant = product.AddVariant(item.SellerSku, "Default", null, 0, now);
            variant.AddOption("Title", "Default", now);

            var inventory = InventoryRecord.Create(variant.Id, product.Id, seller.Id, 120, 5, now);
            context.Inventory.Add(inventory);

            foreach (var spec in item.Specs)
            {
                var parts = spec.Split(':', 2, StringSplitOptions.TrimEntries);
                product.AddSpecification(parts[0], parts.Length > 1 ? parts[1] : spec, 0, now);
            }

            product.AttachTags([tags[item.Brand ?? "General"]], now);
            product.Approve(now);

            context.Products.Add(product);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// One row per tag name, matched without regard to case because that is how the unique
    /// index treats them.
    /// </summary>
    /// <remarks>
    /// The existing tags are loaded tracked, deliberately. Every product in the catalogue is
    /// tagged with the same handful of instances, and the change tracker decides between
    /// "update this row" and "insert this row" by whether it is tracking the instance. Handing
    /// it an untracked one marks it Added, and the save then fails on the tag primary key the
    /// first time a product is created against a tag that already exists.
    /// </remarks>
    private async Task<Dictionary<string, Tag>> ResolveTagsAsync(
        IEnumerable<string> names, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var wanted = names.Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var existing = await context.Tags.ToListAsync(cancellationToken).ConfigureAwait(false);
        var resolved = new Dictionary<string, Tag>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in existing)
        {
            resolved[tag.Name] = tag;
        }

        var created = new List<Tag>();

        foreach (var name in wanted.Where(name => !resolved.ContainsKey(name)))
        {
            var tag = Tag.Create(name, now);
            created.Add(tag);
            resolved[name] = tag;
        }

        if (created.Count > 0)
        {
            context.Tags.AddRange(created);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return resolved;
    }


    private async Task EnsureCouponsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await context.Coupons.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var welcome = Coupon.Create(null, CouponScope.Global, "WELCOME10", "10% off your first order, up to $25.",
            CouponDiscountType.Percentage, 10m, 50m, 25m, 1000, 1,
            now.AddDays(-30), now.AddDays(180), now);

        var flat = Coupon.Create(null, CouponScope.Global, "FLAT15", "$15 off orders over $120.",
            CouponDiscountType.FixedAmount, 15m, 120m, null, 500, 2,
            now.AddDays(-30), now.AddDays(90), now);

        context.Coupons.AddRange(welcome, flat);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureAddressAsync(User customer, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await context.UserAddresses.AnyAsync(a => a.UserId == customer.Id, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var address = UserAddress.Create(
            customer.Id, "Home", customer.FullName, "+9779800000000",
            "12 Ratna Marg, Sundhara", "Ward 10", "Kathmandu", "Bagmati", "44600", "NP", true, now);

        context.UserAddresses.Add(address);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
