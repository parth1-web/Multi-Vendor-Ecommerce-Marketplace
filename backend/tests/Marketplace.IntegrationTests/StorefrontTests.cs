using System.Net;
using FluentAssertions;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Catalog.DTOs;
using Marketplace.Application.Modules.Sellers.DTOs;
using Marketplace.IntegrationTests.Infrastructure;
using Xunit;

namespace Marketplace.IntegrationTests;

/// <summary>
/// The public storefront.
///
/// A storefront is the one page a seller cannot fix themselves, and the figures on it are read
/// by shoppers deciding whether to buy. The class has a database of its own because one of its
/// cases deletes a product: a shared seed would leave every other test in the class resolving a
/// product that is no longer there.
/// </summary>
public sealed class StorefrontTests : IClassFixture<MarketplaceApiFactory>, IAsyncLifetime
{
    private readonly MarketplaceApiFactory _factory;
    private MarketplaceTestData _data = null!;
    private ApiClient _shopper = null!;

    public StorefrontTests(MarketplaceApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _data = await _factory.CreateDatabaseAsync();
        await _data.InitialiseAsync();

        _shopper = new ApiClient(_factory.CreateClient());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_storefront_counts_the_products_it_actually_sells()
    {
        var profile = await _shopper.GetAsync<StoreProfileResponse>($"/api/stores/{_data.SellerAStoreSlug}");

        profile.Should().NotBeNull();
        profile!.ProductCount.Should().Be(2, "the count comes from the listing the storefront is showing");
        profile.Products.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task A_storefront_shows_the_same_catalogue_the_browse_page_would()
    {
        var profile = await _shopper.GetAsync<StoreProfileResponse>($"/api/stores/{_data.SellerAStoreSlug}");
        var listing = await _shopper.GetAsync<PagedResult<ProductSummaryResponse>>($"/api/products?sellerSlug={_data.SellerAStoreSlug}&pageSize=50");

        profile.Should().NotBeNull();
        listing.Should().NotBeNull();

        // The cards on a storefront are the catalogue's cards. If the two are built separately
        // they drift, and the storefront is where a shopper reads stock and category first.
        profile!.Products.Items.Select(p => p.Id).Should().BeEquivalentTo(listing!.Items.Select(p => p.Id));
        profile.Products.Items.Should().OnlyContain(p => p.CategorySlug == "electronics");
        profile.Products.Items.Should().OnlyContain(p => p.AvailableQuantity > 0);
    }

    [Fact]
    public async Task A_storefront_stops_counting_a_product_that_leaves_the_catalogue()
    {
        var (seller, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);

        var removed = await seller.Http.DeleteAsync($"/api/seller/products/{_data.SingleUnitProductId:N}");
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent, await ApiClient.ReadTextAsync(removed));

        var profile = await _shopper.GetAsync<StoreProfileResponse>($"/api/stores/{_data.SellerAStoreSlug}");

        profile.Should().NotBeNull();
        profile!.ProductCount.Should().Be(1, "a deleted product is no longer part of the store's catalogue");
        profile.Products.Items.Select(p => p.Id).Should().NotContain(_data.SingleUnitProductId);
    }

    [Fact]
    public async Task An_unknown_store_slug_is_a_not_found()
    {
        var response = await _shopper.Http.GetAsync("/api/stores/no-such-store");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_suspended_sellers_storefront_is_a_not_found()
    {
        var response = await _shopper.Http.GetAsync("/api/stores/suspended-store");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a suspended seller has no storefront to show, however good the slug still looks");
    }
}
