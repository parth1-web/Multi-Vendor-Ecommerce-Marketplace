using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Notifications.DTOs;
using Marketplace.Application.Modules.Orders.DTOs;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Marketplace.IntegrationTests;

/// <summary>
/// Stock is the one thing a marketplace cannot get wrong twice. These scenarios cover who may
/// touch it, that every movement leaves a ledger entry, and that two customers racing for the
/// last unit produce one order rather than an oversold catalogue.
/// </summary>
public sealed class InventoryTests : IClassFixture<MarketplaceApiFactory>, IAsyncLifetime
{
    private readonly MarketplaceApiFactory _factory;
    private MarketplaceTestData _data = null!;

    public InventoryTests(MarketplaceApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _data = await _factory.CreateDatabaseAsync();
        await _data.InitialiseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_seller_sees_only_the_stock_of_their_own_products()
    {
        var (sellerA, _) = await SignInSellerAsync(MarketplaceTestData.SellerEmail);
        var (sellerB, _) = await SignInSellerAsync(MarketplaceTestData.SecondSellerEmail);

        var mine = await sellerA.GetAsync<PagedResult<InventoryItemResponse>>("/api/inventory");
        var theirs = await sellerB.GetAsync<PagedResult<InventoryItemResponse>>("/api/inventory");

        mine!.Items.Should().Contain(i => i.ProductVariantId == _data.SellerAProductVariantId);
        mine.Items.Should().NotContain(i => i.ProductId == _data.SellerBProductId, "another seller's stock is not this seller's business");

        // A stock list of blank names and the year 1 is what a seller sees on their own page,
        // so the names and the date are asserted rather than assumed.
        mine.Items.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i.ProductName), "a row a seller cannot identify is not a stock list");
        mine.Items.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i.Sku));
        mine.Items.Should().OnlyContain(i => i.UpdatedAt > DateTimeOffset.UnixEpoch, "stock that has never been moved is still dated today");

        theirs!.Items.Should().Contain(i => i.ProductVariantId == _data.SellerBProductVariantId);
        theirs.Items.Should().NotContain(i => i.ProductVariantId == _data.SellerAProductVariantId);
    }

    [Fact]
    public async Task A_customer_cannot_read_or_change_stock()
    {
        var (customer, _) = await SignInCustomerAsync();

        (await customer.Http.GetAsync("/api/inventory")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var adjust = await customer.PutAsync(
            $"/api/inventory/{_data.SellerAProductVariantId}",
            new AdjustStockRequest(100, "not my call"));

        adjust.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AvailableAsync(_data.SellerAProductVariantId)).Should().Be(25);
    }

    [Fact]
    public async Task A_seller_cannot_change_stock_that_belongs_to_another_seller()
    {
        var (sellerB, _) = await SignInSellerAsync(MarketplaceTestData.SecondSellerEmail);

        var response = await sellerB.PutAsync(
            $"/api/inventory/{_data.SellerAProductVariantId}",
            new AdjustStockRequest(100, "not my product"));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.UnprocessableEntity);
        (await AvailableAsync(_data.SellerAProductVariantId)).Should().Be(25, "the quantity must be untouched");
    }

    [Fact]
    public async Task Adjusting_stock_records_the_movement_with_the_reason_given()
    {
        var (sellerA, _) = await SignInSellerAsync(MarketplaceTestData.SellerEmail);

        var before = await AvailableAsync(_data.SellerAProductVariantId);

        var response = await sellerA.PutAsync(
            $"/api/inventory/{_data.SellerAProductVariantId}",
            new AdjustStockRequest(7, "delivery arrived"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await AvailableAsync(_data.SellerAProductVariantId)).Should().Be(before + 7);

        var ledger = await sellerA.GetAsync<List<InventoryTransactionResponse>>(
            $"/api/inventory/{_data.SellerAProductVariantId}/transactions");

        ledger.Should().NotBeNull();
        var movement = ledger!.Single(t => t.Reason == "delivery arrived");
        // A positive delta is recorded as a restock, which is what it is.
        movement.Type.Should().Be(InventoryTransactionType.Restock);
        movement.QuantityDelta.Should().Be(7);
        movement.QuantityAfter.Should().Be(before + 7);
        (movement.QuantityAfter - movement.QuantityBefore).Should().Be(7, "the ledger must show the same move the quantity did");
        movement.Reason.Should().Be("delivery arrived");
    }

    [Fact]
    public async Task Stock_cannot_be_driven_negative()
    {
        var (sellerA, _) = await SignInSellerAsync(MarketplaceTestData.SellerEmail);

        var response = await sellerA.PutAsync(
            $"/api/inventory/{_data.SellerAProductVariantId}",
            new AdjustStockRequest(-1000, "typo"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await AvailableAsync(_data.SellerAProductVariantId)).Should().Be(25);
    }

    [Fact]
    public async Task A_seller_can_raise_the_low_stock_threshold_and_then_see_the_item_flagged()
    {
        var (sellerA, _) = await SignInSellerAsync(MarketplaceTestData.SellerEmail);

        var threshold = await sellerA.PutAsync(
            $"/api/inventory/{_data.SellerAProductVariantId}/threshold",
            new SetThresholdRequest(30));

        threshold.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var lowStock = await sellerA.GetAsync<List<InventoryItemResponse>>("/api/inventory/low-stock");

        lowStock.Should().NotBeNull();
        lowStock!.Should().Contain(i => i.ProductVariantId == _data.SellerAProductVariantId);
    }

    [Fact]
    public async Task The_last_unit_cannot_be_sold_twice()
    {
        // The seeded variant has exactly one unit, so two customers racing for it must produce
        // one order. A conditional UPDATE is the only thing standing between them and an
        // oversold catalogue, which is why this asserts on the ledger and not on the response.
        var (first, _) = await SignInCustomerAsync();
        var (second, _) = await SignInCustomerAsync();

        var firstAdd = await first.PostAsync("/api/cart/items", new AddCartItemRequest(
            _data.SingleUnitProductId, _data.SingleUnitVariantId, 1));
        var secondAdd = await second.PostAsync("/api/cart/items", new AddCartItemRequest(
            _data.SingleUnitProductId, _data.SingleUnitVariantId, 1));

        firstAdd.StatusCode.Should().Be(HttpStatusCode.OK, await ApiClient.ReadTextAsync(firstAdd));
        secondAdd.StatusCode.Should().Be(HttpStatusCode.OK, await ApiClient.ReadTextAsync(secondAdd));

        var firstAddress = await CheckoutForAsync(first);
        var secondAddress = await CheckoutForAsync(second);

        var firstOrder = await first.PostAsync("/api/checkout",
            new CheckoutRequest(firstAddress, "Mock", null, null, null, null));
        var secondOrder = await second.PostAsync("/api/checkout",
            new CheckoutRequest(secondAddress, "Mock", null, null, null, null));

        var statuses = new[]
        {
            firstOrder.StatusCode,
            secondOrder.StatusCode
        };
        statuses.Should().ContainSingle(s => s == HttpStatusCode.Created,
            $"first: {(int)firstOrder.StatusCode} second: {(int)secondOrder.StatusCode}");
        statuses.Should().ContainSingle(s => s == HttpStatusCode.UnprocessableEntity);

        (await ReservedAsync(_data.SingleUnitVariantId)).Should().Be(1);
        (await SoldAsync(_data.SingleUnitVariantId)).Should().Be(0, "the sale settles when the payment lands");
    }

    [Fact]
    public async Task Paying_for_an_order_turns_the_hold_into_a_sale_in_the_ledger()
    {
        var (customer, _) = await SignInCustomerAsync();
        var order = await CheckoutAsync(customer);

        var reserved = await ReservedAsync(_data.SellerAProductVariantId);

        var (seller, _) = await SignInSellerAsync(MarketplaceTestData.SellerEmail);
        var ledgerBefore = await seller.GetAsync<List<InventoryTransactionResponse>>(
            $"/api/inventory/{_data.SellerAProductVariantId}/transactions");

        await WebhookHelper.SettlePaymentAsync(_factory, customer, order.OrderId, "inventory-sale");

        var ledgerAfter = await seller.GetAsync<List<InventoryTransactionResponse>>(
            $"/api/inventory/{_data.SellerAProductVariantId}/transactions");

        ledgerAfter!.Count.Should().BeGreaterThan(ledgerBefore!.Count, "settling must write to the ledger");

        var sale = ledgerAfter.First(t => t.Type == InventoryTransactionType.Sale);
        sale.QuantityDelta.Should().BeNegative();
        sale.Reason.Should().Be("payment-settled");
        sale.ReferenceId.Should().Be(order.OrderId);

        (await ReservedAsync(_data.SellerAProductVariantId)).Should().BeLessThan(reserved);
    }

    [Fact]
    public async Task An_admin_can_see_stock_across_every_seller()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var all = await admin.GetAsync<PagedResult<InventoryItemResponse>>("/api/inventory?pageSize=100");

        all!.Items.Should().Contain(i => i.ProductId == _data.SellerAProductId);
        all.Items.Should().Contain(i => i.ProductId == _data.SellerBProductId);
    }

    [Fact]
    public async Task An_admin_can_adjust_stock_on_a_sellers_behalf()
    {
        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var response = await admin.PutAsync(
            $"/api/inventory/{_data.SellerBProductVariantId}",
            new AdjustStockRequest(3, "stock take correction"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private Task<(ApiClient Client, SessionTokens Tokens)> SignInSellerAsync(string email) =>
        AuthHelper.SignInAsync(_factory, email, MarketplaceTestData.SellerPassword);

    private async Task<(ApiClient Client, Guid AddressId)> SignInCustomerAsync()
    {
        var email = $"inventory-{Guid.NewGuid():N}@test.dev";
        var (client, _) = await AuthHelper.RegisterAsync(_factory, email, MarketplaceTestData.CustomerPassword);

        var address = await client.PostAsync("/api/addresses", new CreateAddressRequest(
            "Home", "Test Customer", "+9779800000000", "12 Ratna Marg", null, "Kathmandu", null, "44600", "NP", true));

        address.StatusCode.Should().Be(HttpStatusCode.Created);
        return (client, (await ApiClient.ReadAsync<AddressResponse>(address))!.Id);
    }

    private async Task<Guid> CheckoutForAsync(ApiClient customer)
    {
        var addresses = await customer.GetAsync<List<AddressResponse>>("/api/addresses");
        return addresses![0].Id;
    }

    private async Task<CheckoutResponse> CheckoutAsync(ApiClient customer)
    {
        await customer.PostAsync("/api/cart/items", new AddCartItemRequest(
            _data.SellerAProductId, _data.SellerAProductVariantId, 1));

        var response = await customer.PostAsync("/api/checkout",
            new CheckoutRequest(await CheckoutForAsync(customer), "Mock", null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await ApiClient.ReadAsync<CheckoutResponse>(response))!;
    }

    private async Task<int> ReadAsync(Guid variantId, Func<Marketplace.Domain.Inventory.Inventory, int> read)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var inventory = await context.Inventory.AsNoTracking().SingleAsync(i => i.ProductVariantId == variantId);
        return read(inventory);
    }

    private Task<int> AvailableAsync(Guid variantId) => ReadAsync(variantId, i => i.AvailableQuantity);

    private Task<int> ReservedAsync(Guid variantId) => ReadAsync(variantId, i => i.ReservedQuantity);

    private Task<int> SoldAsync(Guid variantId) => ReadAsync(variantId, i => i.SoldQuantity);
}
