using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Catalog.DTOs;
using Marketplace.IntegrationTests.Infrastructure;
using Marketplace.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Marketplace.IntegrationTests;

/// <summary>
/// A seller may only ever see and change their own catalogue, and the roles around them must
/// be closed off. These scenarios assert the boundary, not the happy path.
/// </summary>
public sealed class SellerAuthorizationTests : IClassFixture<MarketplaceApiFactory>, IAsyncLifetime
{
    private readonly MarketplaceApiFactory _factory;
    private MarketplaceTestData _data = null!;

    public SellerAuthorizationTests(MarketplaceApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _data = await _factory.CreateDatabaseAsync();
        await _data.InitialiseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task An_anonymous_visitor_cannot_reach_the_seller_catalogue()
    {
        var client = new ApiClient(_factory.CreateClient());

        var response = await client.Http.GetAsync("/api/seller/products");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_customer_cannot_reach_the_seller_catalogue()
    {
        var (client, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.CustomerEmail, MarketplaceTestData.CustomerPassword);

        var response = await client.Http.GetAsync("/api/seller/products");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_seller_sees_only_their_own_products()
    {
        var (sellerA, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);
        var (sellerB, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SecondSellerEmail, MarketplaceTestData.SellerPassword);

        var own = await sellerA.GetAsync<PagedResult<SellerProductListItemResponse>>("/api/seller/products");
        var others = await sellerB.GetAsync<PagedResult<SellerProductListItemResponse>>("/api/seller/products");

        own.Should().NotBeNull();
        own!.Items.Should().OnlyContain(p => p.Name.Length > 0);
        own.Items.Should().Contain(p => p.Id == _data.SellerAProductId);
        own.Items.Should().NotContain(p => p.Id == _data.SellerBProductId);

        others!.Items.Should().Contain(p => p.Id == _data.SellerBProductId);
        others.Items.Should().NotContain(p => p.Id == _data.SellerAProductId);
    }

    [Fact]
    public async Task A_seller_sees_the_status_of_their_own_products()
    {
        var (sellerA, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);

        var own = await sellerA.GetAsync<PagedResult<SellerProductListItemResponse>>("/api/seller/products");

        own.Should().NotBeNull();
        own!.Items.Should().NotBeEmpty();
        own.Items.Should().OnlyContain(p => p.Status == ProductStatus.Published || p.Status == ProductStatus.PendingApproval || p.Status == ProductStatus.Draft);
        own.Items.Should().NotContain(p => p.Status == ProductStatus.Archived,
            "the seller's own catalogue carries a status, which the public shape deliberately does not");
    }


    [Fact]
    public async Task A_seller_cannot_update_a_product_that_belongs_to_someone_else()
    {
        var (sellerB, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SecondSellerEmail, MarketplaceTestData.SellerPassword);

        var response = await sellerB.PutAsync($"/api/seller/products/{_data.SellerAProductId}", UpdateRequest(_data.CategoryId, "Hijacked"));

        // The product must be treated as absent for the wrong seller rather than updated.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.UnprocessableEntity);
        (await ApiClient.ReadTextAsync(response)).Should().NotContain("Hijacked");

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Marketplace.Infrastructure.Persistence.MarketplaceDbContext>();
        var product = await context.Products.AsNoTracking().FirstAsync(p => p.Id == _data.SellerAProductId);
        product.Name.Should().NotBe("Hijacked");
    }

    [Fact]
    public async Task A_seller_cannot_delete_a_product_that_belongs_to_someone_else()
    {
        var (sellerB, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SecondSellerEmail, MarketplaceTestData.SellerPassword);

        var response = await sellerB.DeleteAsync($"/api/seller/products/{_data.SellerAProductId}");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.UnprocessableEntity);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Marketplace.Infrastructure.Persistence.MarketplaceDbContext>();
        (await context.Products.AsNoTracking().AnyAsync(p => p.Id == _data.SellerAProductId)).Should().BeTrue();
    }

    [Fact]
    public async Task A_seller_cannot_approve_their_own_product()
    {
        var (sellerA, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);

        var created = await sellerA.PostAsync("/api/seller/products", CreateRequest("Approval Boundary"));

        created.StatusCode.Should().Be(HttpStatusCode.Created, await ApiClient.ReadTextAsync(created));
        var product = await ApiClient.ReadAsync<ProductSummaryResponse>(created);
        product!.SellerId.Should().Be(_data.SellerAId);

        // A seller cannot publish: creation puts the product straight into review.
        (await StatusOfAsync(product.Id)).Should().Be(ProductStatus.PendingApproval);

        var approval = await sellerA.PutAsync(
            $"/api/admin/products/{product.Id}/approval",
            new ProductApprovalRequest(true));

        approval.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);

        // The self-approval attempt must leave the product exactly where it was.
        (await StatusOfAsync(product.Id)).Should().Be(ProductStatus.PendingApproval);
    }

    [Fact]
    public async Task An_admin_can_approve_a_submitted_product()
    {
        var (sellerA, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);
        var (admin, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var created = await sellerA.PostAsync("/api/seller/products", CreateRequest("Approved By Admin"));
        var product = await ApiClient.ReadAsync<ProductSummaryResponse>(created);

        // Creation puts a product straight into review, so the admin has something to act on.
        (await StatusOfAsync(product!.Id)).Should().Be(ProductStatus.PendingApproval);

        var approval = await admin.PutAsync(
            $"/api/admin/products/{product.Id}/approval",
            new ProductApprovalRequest(true));

        approval.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StatusOfAsync(product.Id)).Should().Be(ProductStatus.Published);
    }

    [Fact]
    public async Task A_seller_cannot_read_the_admin_user_list()
    {
        var (sellerA, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);

        var response = await sellerA.Http.GetAsync("/api/admin/users");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_suspended_seller_is_refused_by_the_active_seller_policy()
    {
        var (suspended, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SuspendedSellerEmail, MarketplaceTestData.SellerPassword);

        var response = await suspended.PostAsync("/api/seller/products", CreateRequest("Should Not Exist"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_pending_seller_is_refused_by_the_active_seller_policy()
    {
        var (pending, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.PendingSellerEmail, MarketplaceTestData.SellerPassword);

        var response = await pending.PostAsync("/api/seller/products", CreateRequest("Should Not Exist Either"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Reads a product's status straight from the database, bypassing the API.</summary>
    private async Task<ProductStatus> StatusOfAsync(Guid productId)
    {
        if (productId == Guid.Empty)
        {
            throw new InvalidOperationException("no product id");
        }

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Marketplace.Infrastructure.Persistence.MarketplaceDbContext>();
        var all = await context.Products.AsNoTracking().Select(p => p.Id.ToString()).ToListAsync();
        if (all.Count == 0)
        {
            throw new InvalidOperationException("no products at all");
        }

        return await context.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.Status).FirstAsync();
    }

    private CreateProductRequest CreateRequest(string name) => new(
        name,
        null,
        $"{name} short",
        $"{name} long description",
        _data.CategoryId,
        120m,
        null,
        "Boundary",
        "M1",
        [new CreateProductImageRequest("https://cdn.test/boundary.jpg", name, true)],
        [new CreateProductVariantRequest($"SKU-{Guid.NewGuid():N}"[..20], "Default", null, 5, 2, [])],
        [new CreateProductSpecificationRequest("Colour", "Black")],
        // Tag names are unique across the marketplace, so every product needs its own.
        [$"boundary-{Guid.NewGuid():N}"[..20]]);

    private static UpdateProductRequest UpdateRequest(Guid categoryId, string name) => new(
        name,
        null,
        $"{name} short",
        $"{name} long",
        categoryId,
        1m,
        null,
        null,
        null,
        null);
}
