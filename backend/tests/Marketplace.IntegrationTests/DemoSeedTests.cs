using FluentAssertions;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Modules.Auth.Abstractions;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Infrastructure.Persistence.Seeding;
using Marketplace.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Marketplace.IntegrationTests;

/// <summary>
/// The demo seed.
///
/// It runs on every development start, so the two properties that matter are that it never
/// duplicates anything and that it heals a catalogue something has removed. The first was
/// originally a guard on the whole table, which meant a developer's own product stopped the demo
/// catalogue appearing, and a deleted demo product was never restored: both are what these cases
/// are about.
/// </summary>
public sealed class DemoSeedTests : IClassFixture<MarketplaceApiFactory>, IAsyncLifetime
{
    private readonly MarketplaceApiFactory _factory;

    public DemoSeedTests(MarketplaceApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.CreateDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Running_the_seed_twice_changes_nothing_the_second_time()
    {
        await SeedAsync();
        var afterFirst = await SnapshotAsync();

        await SeedAsync();
        var afterSecond = await SnapshotAsync();

        afterSecond.Should().BeEquivalentTo(afterFirst, "a seed that runs on every start must not duplicate itself");
    }

    [Fact]
    public async Task A_product_of_my_own_does_not_stop_the_demo_catalogue()
    {
        await SeedAsync();
        var before = await SnapshotAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            var seller = await context.Sellers.FirstAsync();
            var category = Category.Create("Mine", Slug.Create("mine"), "mine", null, 0, DateTimeOffset.UtcNow);
            context.Categories.Add(category);
            context.Products.Add(Product.Create(
                seller.Id, category.Id, "My own product", Slug.Create($"mine-{Guid.NewGuid():N}"),
                "Mine", "A product of my own.", 10m, null, null, null, DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
        }

        await SeedAsync();

        var after = await SnapshotAsync();
        after.Products.Should().Be(before.Products + 1, "the demo catalogue is added to, not replaced by, what a developer already has");
        after.Categories.Should().Be(before.Categories + 1);

        using var verify = _factory.Services.CreateScope();
        var verifyContext = verify.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var demoStillThere = await verifyContext.Products.CountAsync(p => p.SlugValue == "aerolux-wireless-headphones" || p.SlugValue == "titan-elite-smartwatch");
        demoStillThere.Should().Be(2, "and the demo listings it would otherwise have skipped are present");
    }


    [Fact]
    public async Task A_deleted_demo_product_comes_back_and_the_rest_are_left_alone()
    {
        await SeedAsync();
        var before = await SnapshotAsync();

        Guid removedId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            var product = await context.Products.FirstAsync(p => p.SlugValue == "aerolux-wireless-headphones");
            removedId = product.Id;
            context.Products.Remove(product);
            await context.SaveChangesAsync();
        }

        await SeedAsync();

        using var verify = _factory.Services.CreateScope();
        var verifyContext = verify.ServiceProvider.GetRequiredService<MarketplaceDbContext>();

        var slugs = await verifyContext.Products.Select(p => p.SlugValue).ToListAsync();
        slugs.Should().Contain("aerolux-wireless-headphones", "a demo product that was removed is restored");
        slugs.Distinct().Count().Should().Be(slugs.Count, "and nothing is added twice in the process");

        var restored = await verifyContext.Products.AsNoTracking().FirstAsync(p => p.SlugValue == "aerolux-wireless-headphones");
        restored.Id.Should().NotBe(removedId, "the row is new rather than resurrected");
        (await verifyContext.Products.CountAsync()).Should().Be(before.Products, "the count comes back to where it was");
    }


    [Fact]
    public async Task The_seeded_storefronts_and_categories_say_how_many_products_they_sell()
    {
        await SeedAsync();

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();

        var stores = await context.SellerStores.AsNoTracking().ToListAsync();
        stores.Should().NotBeEmpty();

        foreach (var store in stores)
        {
            var published = await context.Products.CountAsync(
                p => p.SellerId == store.SellerId && p.Status == Marketplace.Domain.Enums.ProductStatus.Published);
            store.ProductCount.Should().Be(published, $"{store.Name} advertises a count a shopper can check");
        }

        var categories = await context.Categories.AsNoTracking().ToListAsync();
        categories.Should().NotBeEmpty();

        foreach (var category in categories)
        {
            var published = await context.Products.CountAsync(
                p => p.CategoryId == category.Id && p.Status == Marketplace.Domain.Enums.ProductStatus.Published);
            category.ProductCount.Should().Be(published, $"{category.Name} does not read as empty while showing products");
        }
    }


    private async Task<SeedSnapshot> SnapshotAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();

        return new SeedSnapshot(
            await context.Users.CountAsync(),
            await context.Sellers.CountAsync(),
            await context.SellerStores.CountAsync(),
            await context.Categories.CountAsync(),
            await context.Products.CountAsync(),
            await context.ProductVariants.CountAsync(),
            await context.Coupons.CountAsync());
    }

    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        var seeder = new DatabaseSeeder(
            services.GetRequiredService<MarketplaceDbContext>(),
            services.GetRequiredService<IPasswordHasher>(),
            _factory.Clock,
            Options.Create(new Marketplace.Application.Modules.Auth.Abstractions.MarketplaceOptions()),
            NullLogger<DatabaseSeeder>.Instance);

        await seeder.SeedAsync();
    }

    private sealed record SeedSnapshot(int Users, int Sellers, int Stores, int Categories, int Products, int Variants, int Coupons);
}
