using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Orders.DTOs;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Marketplace.IntegrationTests;

/// <summary>
/// The basket-to-order path: pricing, stock reservation, the split per seller and the
/// guarantees a retry has to keep. Stock is asserted in the database, because an order that
/// looks right while the inventory ledger disagrees is not right.
/// </summary>
public sealed class CheckoutTests : IClassFixture<MarketplaceApiFactory>, IAsyncLifetime
{
    private readonly MarketplaceApiFactory _factory;
    private MarketplaceTestData _data = null!;

    public CheckoutTests(MarketplaceApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _data = await _factory.CreateDatabaseAsync();
        await _data.InitialiseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Adding_an_item_puts_it_in_the_cart_with_a_running_total()
    {
        var (customer, addressId) = await NewCustomerAsync();

        var response = await customer.PostAsync("/api/cart/items", new AddCartItemRequest(
            _data.SellerAProductId, _data.SellerAProductVariantId, 2));

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"status={(int)response.StatusCode}");
        }

        var cart = await ApiClient.ReadAsync<CartResponse>(response);

        cart!.ItemCount.Should().Be(1);
        cart.TotalQuantity.Should().Be(2);
        cart.Subtotal.Should().Be(498m);
        cart.Groups.Should().ContainSingle(g => g.SellerId == _data.SellerAId);
    }

    [Fact]
    public async Task A_cart_refuses_more_items_than_the_variant_holds()
    {
        var (customer, addressId) = await NewCustomerAsync();

        // The seeded variant has a single unit left.
        var response = await customer.PostAsync("/api/cart/items", new AddCartItemRequest(
            _data.SellerAProductId, _data.SingleUnitVariantId, 5));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ApiClient.ReadTextAsync(response)).Should().Contain("available");
    }

    [Fact]
    public async Task A_quote_prices_the_basket_without_writing_anything()
    {
        var (customer, addressId) = await NewCustomerAsync();
        await customer.PostAsync("/api/cart/items", new AddCartItemRequest(
            _data.SellerAProductId, _data.SellerAProductVariantId, 1));

        var before = await OrderCountAsync();

        var response = await customer.PostAsync("/api/checkout/quote",
            new QuoteRequest(addressId, null, null, true));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await ApiClient.ReadTextAsync(response));
        var quote = await ApiClient.ReadAsync<QuoteResponse>(response);

        quote!.Subtotal.Should().Be(249m);
        quote.DiscountAmount.Should().Be(0m);
        quote.TotalAmount.Should().BeGreaterThan(0m);
        quote.SellerBreakdown.Should().ContainSingle();

        (await OrderCountAsync()).Should().Be(before, "a quote must not place an order");
    }

    [Fact]
    public async Task A_quote_applies_a_valid_coupon()
    {
        var (customer, addressId) = await NewCustomerAsync();
        await customer.PostAsync("/api/cart/items", new AddCartItemRequest(
            _data.SellerAProductId, _data.SellerAProductVariantId, 1));

        var response = await customer.PostAsync("/api/checkout/quote",
            new QuoteRequest(addressId, "TEST10", null, true));

        var quote = await ApiClient.ReadAsync<QuoteResponse>(response);

        quote!.DiscountAmount.Should().BeGreaterThan(0m);
        quote.Coupon.Should().NotBeNull();
        quote.Coupon!.Code.Should().Be("TEST10");
        quote.TotalAmount.Should().Be(quote.Subtotal - quote.DiscountAmount + quote.ShippingAmount + quote.TaxAmount);
    }

    [Fact]
    public async Task Checking_out_creates_one_order_split_per_seller_and_reserves_stock()
    {
        var (customer, addressId) = await NewCustomerAsync();

        await customer.PostAsync("/api/cart/items", new AddCartItemRequest(
            _data.SellerAProductId, _data.SellerAProductVariantId, 2));
        await customer.PostAsync("/api/cart/items", new AddCartItemRequest(
            _data.SellerBProductId, _data.SellerBProductVariantId, 1));

        var availableBefore = await AvailableAsync(_data.SellerAProductVariantId);
        var reservedBefore = await ReservedAsync(_data.SellerAProductVariantId);
        var sellableBefore = await SellableAsync(_data.SellerAProductVariantId);

        var response = await customer.PostAsync("/api/checkout", Checkout(addressId));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var placed = await ApiClient.ReadAsync<CheckoutResponse>(response);

        placed!.OrderNumber.Should().StartWith("MP-");
        placed.SellerCount.Should().Be(2);
        placed.Status.Should().Be(OrderStatus.Pending);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();

        var order = await context.Orders.AsNoTracking()
            .Include(o => o.SellerOrders)
            .ThenInclude(so => so.Items)
            .SingleAsync(o => o.Id == placed.OrderId);

        order.SellerOrders.Should().HaveCount(2);
        order.SellerOrders.Select(so => so.SellerId).Should().BeEquivalentTo(
            new[] { _data.SellerAId, _data.SellerBId });
        order.SellerOrders.Sum(so => so.Subtotal).Should().Be(order.Subtotal);
        order.TotalAmount.Should().Be(placed.TotalAmount);

        // The reservation is what protects the stock, so it must be visible in the ledger:
        // physical stock is untouched, what changes is how much of it can still be sold.
        (await AvailableAsync(_data.SellerAProductVariantId)).Should().Be(availableBefore);
        (await ReservedAsync(_data.SellerAProductVariantId)).Should().Be(reservedBefore + 2);
        (await SellableAsync(_data.SellerAProductVariantId)).Should().Be(sellableBefore - 2, "the two ordered units are no longer sellable");
    }

    [Fact]
    public async Task Checking_out_empties_the_cart()
    {
        var (customer, addressId) = await NewCustomerAsync();
        await customer.PostAsync("/api/cart/items", new AddCartItemRequest(
            _data.SellerAProductId, _data.SellerAProductVariantId, 1));

        (await customer.PostAsync("/api/checkout", Checkout(addressId))).StatusCode
            .Should().Be(HttpStatusCode.Created);

        var cart = await customer.GetAsync<CartResponse>("/api/cart");

        cart!.ItemCount.Should().Be(0);
        cart.Subtotal.Should().Be(0m);
    }

    [Fact]
    public async Task Retrying_with_the_same_idempotency_key_returns_the_same_order()
    {
        var (customer, addressId) = await NewCustomerAsync();
        await customer.PostAsync("/api/cart/items", new AddCartItemRequest(
            _data.SellerAProductId, _data.SellerAProductVariantId, 1));

        var ordersBefore = await OrderCountAsync();

        var key = Guid.NewGuid().ToString("N");
        var first = await customer.PostAsync("/api/checkout", Checkout(addressId, key));
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstOrder = await ApiClient.ReadAsync<CheckoutResponse>(first);

        // The cart is empty now, so only the idempotency key can produce a second order.
        var retry = await customer.PostAsync("/api/checkout", Checkout(addressId, key));
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
        var retried = await ApiClient.ReadAsync<CheckoutResponse>(retry);

        retried!.OrderId.Should().Be(firstOrder!.OrderId);
        (await OrderCountAsync()).Should().Be(ordersBefore + 1, "a retried checkout must not create a second order");
    }

    [Fact]
    public async Task The_idempotency_key_is_stored_on_its_own_column_and_leaves_the_coupon_alone()
    {
        var (customer, addressId) = await NewCustomerAsync();
        await customer.PostAsync("/api/cart/items", new AddCartItemRequest(
            _data.SellerAProductId, _data.SellerAProductVariantId, 1));

        var key = Guid.NewGuid().ToString("N");
        var response = await customer.PostAsync("/api/checkout", Checkout(addressId, key, "TEST10"));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var placed = await ApiClient.ReadAsync<CheckoutResponse>(response);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var order = await context.Orders.AsNoTracking().SingleAsync(o => o.Id == placed!.OrderId);

        order.IdempotencyKey.Should().Be(key);
        order.CouponCode.Should().Be("TEST10", "the coupon must not be overwritten by the retry key");
        order.DiscountAmount.Should().BeGreaterThan(0m);
    }

    [Fact]
    public async Task Checking_out_an_empty_cart_is_refused()
    {
        var (customer, addressId) = await NewCustomerAsync();

        var response = await customer.PostAsync("/api/checkout", Checkout(addressId));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ApiClient.ReadTextAsync(response)).Should().Contain("empty");
    }

    [Fact]
    public async Task Checking_out_to_an_address_that_is_not_yours_is_refused()
    {
        var (customer, addressId) = await NewCustomerAsync();
        await customer.PostAsync("/api/cart/items", new AddCartItemRequest(
            _data.SellerAProductId, _data.SellerAProductVariantId, 1));

        var foreignAddress = await CreateForeignAddressAsync();

        var before = await OrderCountAsync();

        var response = await customer.PostAsync("/api/checkout", Checkout(foreignAddress));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await OrderCountAsync()).Should().Be(before);
    }

    [Fact]
    public async Task Checking_out_requires_a_signed_in_customer()
    {
        var anonymous = new ApiClient(_factory.CreateClient());
        await anonymous.PostAsync("/api/cart/items", new AddCartItemRequest(
            _data.SellerAProductId, _data.SellerAProductVariantId, 1));

        var response = await anonymous.Http.PostAsJsonAsync("/api/checkout", Checkout(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_customer_only_sees_their_own_orders()
    {
        var (customer, customerAddress) = await NewCustomerAsync();

        // A second account, so "only your own orders" is actually tested.
        var (other, _) = await NewCustomerAsync();

        await customer.PostAsync("/api/cart/items", new AddCartItemRequest(
            _data.SellerAProductId, _data.SellerAProductVariantId, 1));
        var placed = await customer.PostAsync("/api/checkout", Checkout(customerAddress));
        var order = await ApiClient.ReadAsync<CheckoutResponse>(placed);

        var mine = await customer.GetAsync<PagedResult<OrderListItemResponse>>("/api/orders");
        var theirs = await other.GetAsync<PagedResult<OrderListItemResponse>>("/api/orders");

        mine!.Items.Should().ContainSingle(o => o.Id == order!.OrderId);
        theirs!.Items.Should().NotContain(o => o.Id == order!.OrderId);

        var peek = await other.Http.GetAsync($"/api/orders/{order!.OrderId}");
        peek.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Every scenario gets its own account, so one test's basket can never leak into the next.
    /// A shared customer would make the totals depend on test order, which is exactly the sort
    /// of coupling that turns a real regression into a mystery.
    /// </summary>
    private async Task<(ApiClient Client, Guid AddressId)> NewCustomerAsync()
    {
        var email = $"checkout-{Guid.NewGuid():N}@test.dev";
        var (client, _) = await AuthHelper.RegisterAsync(_factory, email, MarketplaceTestData.CustomerPassword);

        var address = await client.PostAsync("/api/addresses", new CreateAddressRequest(
            "Home", "Test Customer", "+9779800000000", "12 Ratna Marg", null, "Kathmandu", null, "44600", "NP", true));

        address.StatusCode.Should().Be(HttpStatusCode.Created);

        return (client, (await ApiClient.ReadAsync<AddressResponse>(address))!.Id);
    }

    private static CheckoutRequest Checkout(Guid addressId, string? idempotencyKey = null, string? coupon = null) =>
        new(addressId, "Mock", coupon, "Integration test order", null, idempotencyKey);

    private async Task<Guid> CreateForeignAddressAsync()
    {
        var (other, _) = await NewCustomerAsync();
        var response = await other.PostAsync("/api/addresses", new CreateAddressRequest(
            "Other", "Sita Thapa", "+9779811111111", "9 Jones Street", null, "Pokhara", null, "33700", "NP", true));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await ApiClient.ReadAsync<AddressResponse>(response))!.Id;
    }

    private async Task<int> OrderCountAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        return await context.Orders.AsNoTracking().CountAsync();
    }

    private async Task<int> AvailableAsync(Guid variantId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        return await context.Inventory.AsNoTracking()
            .Where(i => i.ProductVariantId == variantId)
            .Select(i => i.AvailableQuantity)
            .FirstAsync();
    }

    private async Task<int> SellableAsync(Guid variantId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        return await context.Inventory.AsNoTracking()
            .Where(i => i.ProductVariantId == variantId)
            .Select(i => i.SellableQuantity)
            .FirstAsync();
    }

    private async Task<int> ReservedAsync(Guid variantId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        return await context.Inventory.AsNoTracking()
            .Where(i => i.ProductVariantId == variantId)
            .Select(i => i.ReservedQuantity)
            .FirstAsync();
    }
}
