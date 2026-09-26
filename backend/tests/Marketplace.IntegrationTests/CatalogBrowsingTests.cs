using System.Net;
using FluentAssertions;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Catalog.DTOs;
using Marketplace.Application.Modules.Sellers.DTOs;
using Marketplace.Infrastructure.Persistence;
using Marketplace.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Marketplace.IntegrationTests;

/// <summary>
/// The public listing, tested through the API a shopper actually calls.
///
/// A slug filter that is accepted and then ignored is the worst kind of catalogue bug: the
/// response is a plausible page of products, so nothing looks broken until someone notices the
/// category page is showing the whole marketplace. Every case here therefore asserts the
/// result is *narrower* than the unfiltered catalogue, and that an unknown name narrows it to
/// nothing rather than widening it.
/// </summary>
public sealed class CatalogBrowsingTests : IClassFixture<MarketplaceApiFactory>, IAsyncLifetime
{
    private readonly MarketplaceApiFactory _factory;
    private MarketplaceTestData _data = null!;
    private ApiClient _shopper = null!;

    public CatalogBrowsingTests(MarketplaceApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _data = await _factory.CreateDatabaseAsync();
        await _data.InitialiseAsync();

        _shopper = new ApiClient(_factory.CreateClient());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_category_slug_returns_only_that_category()
    {
        var unfiltered = await ListAsync();
        var child = await ListAsync("categorySlug=headphones");

        child.TotalCount.Should().Be(1, "only one product is filed under the child category");
        child.Items.Should().ContainSingle().Which.Id.Should().Be(_data.ChildCategoryProductId);
        child.TotalCount.Should().BeLessThan(unfiltered.TotalCount);
    }

    [Fact]
    public async Task A_parent_category_slug_includes_what_is_filed_beneath_it()
    {
        var parent = await ListAsync("categorySlug=electronics");
        var direct = await ListAsync("categorySlug=electronics&includeSubcategories=false");

        parent.TotalCount.Should().Be(direct.TotalCount + 1,
            "the parent's own products plus the one filed under its child");
        parent.Items.Select(p => p.Id).Should().Contain(_data.ChildCategoryProductId);
        direct.Items.Select(p => p.Id).Should().NotContain(_data.ChildCategoryProductId);
    }

    [Fact]
    public async Task An_unknown_category_slug_returns_nothing_rather_than_everything()
    {
        var unfiltered = await ListAsync();
        var missing = await ListAsync("categorySlug=no-such-category");

        missing.TotalCount.Should().Be(0);
        missing.Items.Should().BeEmpty();
        unfiltered.TotalCount.Should().BeGreaterThan(0, "the marketplace itself is not empty");
    }

    [Fact]
    public async Task An_inactive_category_slug_returns_nothing()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            var category = await context.Categories.FirstAsync(c => c.Id == _data.CategoryId);
            category.SetActive(false, DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        try
        {
            var listing = await ListAsync("categorySlug=electronics");

            listing.TotalCount.Should().Be(0, "a hidden category is not browsable, and its products are not reachable through it");
        }
        finally
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            var category = await context.Categories.FirstAsync(c => c.Id == _data.CategoryId);
            category.SetActive(true, DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task A_store_slug_returns_only_that_stores_products()
    {
        var listing = await ListAsync($"sellerSlug={_data.SellerAStoreSlug}");

        listing.TotalCount.Should().Be(2, "seller A sells the headphones and the last unit");
        listing.Items.Select(p => p.SellerId).Should().OnlyContain(id => id == _data.SellerAId);
    }

    [Fact]
    public async Task A_store_slug_belonging_to_a_suspended_seller_returns_nothing()
    {
        var listing = await ListAsync("sellerSlug=suspended-store");

        listing.TotalCount.Should().Be(0, "a suspended seller has no storefront to show");
    }

    [Fact]
    public async Task An_unknown_store_slug_returns_nothing()
    {
        var listing = await ListAsync("sellerSlug=no-such-store");

        listing.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task In_stock_means_sellable_and_not_merely_switched_on()
    {
        var drained = await DrainInventoryAsync(_data.SellerBProductId);

        var listing = await ListAsync("inStock=true");
        var seen = string.Join(", ", listing.Items.Select(p => $"{p.Slug}({p.AvailableQuantity})"));

        listing.Items.Select(p => p.Id).Should().NotContain(_data.SellerBProductId,
            $"a variant that is active but empty cannot be bought, so it is not in stock; drained {drained}, saw {seen}");
        listing.Items.Should().NotBeEmpty("seller A still has stock");
    }

    [Fact]
    public async Task A_product_detail_reads_the_same_scope_as_the_listing()

    {
        var detail = await _shopper.GetAsync<ProductDetailResponse>($"/api/products/slug/{_data.SellerAProductSlug}");

        detail.Should().NotBeNull();
        detail!.StoreSlug.Should().Be(_data.SellerAStoreSlug);
        detail.CategorySlug.Should().Be("electronics");
        detail.Variants.Should().ContainSingle();
    }

    [Fact]
    public async Task An_unknown_product_slug_is_a_not_found_rather_than_a_server_error()
    {
        var response = await _shopper.Http.GetAsync("/api/products/slug/no-such-product");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Empties every inventory row for a product, so it is switched on but unsellable.</summary>
    private async Task<Guid> DrainInventoryAsync(Guid productId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();

        var record = await context.Inventory.FirstAsync(i => i.ProductId == productId);
        record.Adjust(-record.AvailableQuantity, DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();

        return record.ProductVariantId;
    }

    private async Task<PagedResult<ProductSummaryResponse>> ListAsync(string query = "")
    {
        var page = await _shopper.GetAsync<PagedResult<ProductSummaryResponse>>($"/api/products?pageSize=50&{query}");
        page.Should().NotBeNull("the listing endpoint answers a paged envelope");

        return page!;
    }
}
