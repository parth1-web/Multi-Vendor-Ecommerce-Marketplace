using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using FluentAssertions;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Catalog.DTOs;
using Marketplace.Domain.Enums;
using Marketplace.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;


namespace Marketplace.IntegrationTests;

/// <summary>
/// Who may read a product that is not on sale yet.
///
/// A new listing, one waiting for a moderator and one that was sent back are all a seller's
/// unpublished work. The whole catalogue controller is anonymous, which is right for shoppers and
/// exactly the sort of thing that leaks somebody's half-finished product through an id.
/// </summary>
public sealed class ProductVisibilityTests : IClassFixture<MarketplaceApiFactory>, IAsyncLifetime
{
    private readonly MarketplaceApiFactory _factory;
    private MarketplaceTestData _data = null!;
    private ApiClient _anonymous = null!;

    public ProductVisibilityTests(MarketplaceApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _data = await _factory.CreateDatabaseAsync();
        await _data.InitialiseAsync();

        _anonymous = new ApiClient(_factory.CreateClient());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_published_product_is_readable_by_id_by_anyone()
    {
        var response = await _anonymous.Http.GetAsync($"/api/products/{_data.SellerAProductId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await ApiClient.ReadTextAsync(response));
    }

    [Fact]
    public async Task A_new_listing_is_not_readable_through_the_public_product_route()
    {
        var listing = await CreateListingAsync();

        var response = await _anonymous.Http.GetAsync($"/api/products/{listing.Id}");
        var body = await ApiClient.ReadTextAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            $"a listing awaiting review read through an anonymous endpoint is a seller's unfinished work on show; got {body[..Math.Min(160, body.Length)]}");
    }

    [Fact]
    public async Task A_listing_awaiting_review_is_not_readable_through_the_public_product_route()
    {
        var (seller, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);
        var listing = await CreateListingAsync(seller);

        var submitted = await seller.Http.PostAsync($"/api/seller/products/{listing.Id}/submit", EmptyJson());
        submitted.StatusCode.Should().Be(HttpStatusCode.NoContent, await ApiClient.ReadTextAsync(submitted));

        var response = await _anonymous.Http.GetAsync($"/api/products/{listing.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a moderator's queue is not a public page");
    }

    [Fact]
    public async Task A_seller_can_read_their_own_listing_in_full()
    {
        var (seller, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);
        var listing = await CreateListingAsync(seller);

        var response = await seller.GetAsync<SellerProductDetailResponse>($"/api/seller/products/{listing.Id}");

        response.Should().NotBeNull();
        response!.Status.Should().Be(ProductStatus.PendingApproval, "a new listing goes straight to a moderator");
        response.Images.Should().HaveCount(1, "a seller needs the picture they uploaded, not a public thumbnail");
        response.Variants.Should().ContainSingle();
    }

    [Fact]
    public async Task A_seller_cannot_read_another_sellers_listing()
    {
        var (otherSeller, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SecondSellerEmail, MarketplaceTestData.SellerPassword);
        var listing = await CreateListingAsync();

        var response = await otherSeller.Http.GetAsync($"/api/seller/products/{listing.Id}");

        // Absent rather than forbidden: a "forbidden" answers the question a seller is really
        // asking, which is whether that product exists at all.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_rejection_note_reaches_its_owner_and_nobody_else()
    {
        var (seller, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);
        var listing = await CreateListingAsync(seller);

        await seller.Http.PostAsync($"/api/seller/products/{listing.Id}/submit", EmptyJson());

        var (admin, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);
        var review = await admin.PutAsync(
            $"/api/admin/products/{listing.Id}/approval",
            new ProductApprovalRequest(false, ProductRejectionReason.InaccurateDescription, "The measurements do not match the photographs."));
        review.StatusCode.Should().Be(HttpStatusCode.NoContent, await ApiClient.ReadTextAsync(review));

        var own = await seller.GetAsync<SellerProductDetailResponse>($"/api/seller/products/{listing.Id}");
        own.Should().NotBeNull();
        own!.RejectionNote.Should().Be("The measurements do not match the photographs.");
        own.Status.Should().Be(ProductStatus.Rejected);

        (await _anonymous.Http.GetAsync($"/api/products/{listing.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unpublished_listing_is_not_readable_by_its_slug_either()
    {
        var listing = await CreateListingAsync();

        // A slug is as guessable as an id, and a prettier address is not a reason to publish
        // somebody's unfinished work.
        var bySlug = await _anonymous.Http.GetAsync($"/api/products/slug/{listing.Slug}");
        bySlug.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var (seller, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);
        (await seller.Http.GetAsync($"/api/products/slug/{listing.Slug}")).StatusCode.Should().Be(HttpStatusCode.OK,
            "the seller who owns it can still see it, which is what the edit screen relies on");
    }

    [Fact]
    public async Task An_owner_reading_a_listing_first_does_not_publish_it()
    {
        // The order matters, and it is the order a real session happens in: the seller opens their
        // own listing, which caches it, and a stranger asks for the same listing a moment later.
        // The detail cache is shared between the entitled reader and the public one, so a cache
        // hit has to be checked against the same rule the database is checked against. Reading it
        // as a stranger first, as the test above does, never populates that cache entry at all.
        var (seller, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);
        var listing = await CreateListingAsync(seller);
        await seller.Http.PostAsync($"/api/seller/products/{listing.Id}/submit", EmptyJson());

        var own = await seller.Http.GetAsync($"/api/products/slug/{listing.Slug}");
        own.StatusCode.Should().Be(HttpStatusCode.OK, "the owner may read their own listing");

        var bySlug = await _anonymous.Http.GetAsync($"/api/products/slug/{listing.Slug}");
        bySlug.StatusCode.Should().Be(HttpStatusCode.NotFound, "a cached copy is not a public copy");

        (await _anonymous.Http.GetAsync($"/api/products/{listing.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var (admin, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);
        (await admin.Http.GetAsync($"/api/products/slug/{listing.Slug}")).StatusCode.Should().Be(HttpStatusCode.OK,
            "a moderator is entitled, and the slug route is the one place that asks who is asking");
    }

    [Fact]
    public async Task A_published_listing_stays_readable_after_its_owner_has_read_it()
    {
        // The other half of the same fix: a cache entry that is public does not stop being public
        // because somebody entitled read it first.
        var (seller, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);
        var listing = await CreateListingAsync(seller);
        await seller.Http.PostAsync($"/api/seller/products/{listing.Id}/submit", EmptyJson());

        var (admin, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);
        var approved = await admin.PutAsync(
            $"/api/admin/products/{listing.Id}/approval",
            new ProductApprovalRequest(true, ProductRejectionReason.None, "Fine"));
        approved.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await seller.Http.GetAsync($"/api/products/slug/{listing.Slug}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _anonymous.Http.GetAsync($"/api/products/{listing.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _anonymous.Http.GetAsync($"/api/products/slug/{listing.Slug}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_moderator_sees_the_queue_a_listing_is_waiting_in()

    {
        var (seller, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);
        var listing = await CreateListingAsync(seller);

        var (admin, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);
        var queue = await admin.GetAsync<PagedResult<ProductSummaryResponse>>("/api/admin/products?status=PendingApproval");

        queue.Should().NotBeNull();
        queue!.Items.Should().Contain(p => p.Id == listing.Id,
            "a queue that cannot see the listings waiting in it is not a queue: the status filter has to reach the query");
    }

    [Fact]
    public async Task A_moderator_sees_every_state_of_the_catalogue()
    {
        var (admin, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);
        var listing = await CreateListingAsync();

        var everything = await admin.GetAsync<PagedResult<ProductSummaryResponse>>("/api/admin/products?pageSize=100");
        var publishedOnly = await admin.GetAsync<PagedResult<ProductSummaryResponse>>("/api/admin/products?status=Published");

        everything.Should().NotBeNull();
        everything!.Items.Should().Contain(p => p.Id == listing.Id, "an admin reads the whole catalogue, published or not");
        publishedOnly!.Items.Should().NotContain(p => p.Id == listing.Id, "a published filter still means published");
    }

    [Fact]
    public async Task A_category_counts_a_listing_only_while_it_is_on_sale()
    {
        // The category tree, the category page and the admin category list all read this count off
        // the row rather than counting on the way out. Nothing used to write it, so every category
        // in the application read "0 products" while showing a dozen.
        var (owner, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);
        var listing = await CreateListingAsync(owner);
        var submitted = await owner.PostAsync($"/api/seller/products/{listing.Id}/submit", EmptyJson());
        submitted.StatusCode.Should().Be(HttpStatusCode.NoContent, await ApiClient.ReadTextAsync(submitted));

        // This class shares one database, so the count is whatever the earlier cases left behind
        // and only the change is interesting.
        var baseline = await CategoryCountAsync(_data.CategoryId);
        baseline.Should().Be(await PublishedInCategoryAsync(_data.CategoryId), "the row is kept in step as products are approved");

        var (admin, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);
        var approved = await admin.PutAsync(
            $"/api/admin/products/{listing.Id}/approval",
            new ProductApprovalRequest(true, ProductRejectionReason.None, "Fine"));
        approved.StatusCode.Should().Be(HttpStatusCode.NoContent, await ApiClient.ReadTextAsync(approved));

        (await CategoryCountAsync(_data.CategoryId)).Should().Be(baseline + 1, "approval is what puts a product into the count");

        var removed = await owner.DeleteAsync($"/api/seller/products/{listing.Id}");
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent, await ApiClient.ReadTextAsync(removed));

        (await CategoryCountAsync(_data.CategoryId)).Should().Be(baseline, "and removing it takes the count back down");
    }

    private async Task<int> CategoryCountAsync(Guid categoryId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Marketplace.Infrastructure.Persistence.MarketplaceDbContext>();
        return await context.Categories.AsNoTracking()
            .Where(c => c.Id == categoryId)
            .Select(c => c.ProductCount)
            .SingleAsync();
    }

    private async Task<int> PublishedInCategoryAsync(Guid categoryId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Marketplace.Infrastructure.Persistence.MarketplaceDbContext>();
        return await context.Products.AsNoTracking()
            .CountAsync(p => p.CategoryId == categoryId && p.Status == ProductStatus.Published);
    }

    private async Task<ProductSummaryResponse> CreateListingAsync(ApiClient? seller = null)

    {
        // A customer cannot create a product, so the listing is made by a seller: the case under
        // test is visibility, not the create path.
        var (owner, _) = seller is null
            ? await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword)
            : (seller!, null!);

        var created = await owner.PostAsync("/api/seller/products", Request(_data.CategoryId));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await ApiClient.ReadTextAsync(created));

        return (await ApiClient.ReadAsync<ProductSummaryResponse>(created))!;
    }

    private static StringContent EmptyJson() => new("{}", Encoding.UTF8, "application/json");

    private static CreateProductRequest Request(Guid categoryId) => new(
        "Visibility probe",
        null,
        "A listing that exists only in its seller's account.",
        "This listing exists so the test can check that an unpublished product is readable by its owner and by nobody else.",
        categoryId,
        42m,
        null,
        "ProbeBrand",
        null,
        [new CreateProductImageRequest("https://cdn.test/probe.jpg", "probe", true)],
        [new CreateProductVariantRequest($"SKU-PROBE-{Guid.NewGuid():N}"[..24], "Default", null, 5, 2, [])],
        [],
        []);
}
