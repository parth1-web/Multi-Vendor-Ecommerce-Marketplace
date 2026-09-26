using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Marketplace.Application.Modules.Orders.DTOs;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Payments.DTOs;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Marketplace.IntegrationTests;

/// <summary>
/// Money has to move exactly once, and only when the provider's word is trustworthy. These
/// scenarios cover the signed webhook, its idempotency, the amount cross-check and the refund
/// request/review round trip, asserting against the ledger rather than the response body.
/// </summary>
public sealed class PaymentAndRefundTests : IClassFixture<MarketplaceApiFactory>, IAsyncLifetime
{
    private const string WebhookSecret = "integration-webhook-secret";

    private readonly MarketplaceApiFactory _factory;
    private MarketplaceTestData _data = null!;

    public PaymentAndRefundTests(MarketplaceApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _data = await _factory.CreateDatabaseAsync();
        await _data.InitialiseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Placing_an_order_creates_an_initiated_payment_with_a_unique_reference()
    {
        var (customer, _) = await NewCustomerAsync();

        var (checkout, order) = await PlaceOrderAsync(customer);
        checkout.StatusCode.Should().Be(HttpStatusCode.Created);
        order.Should().NotBeNull();


        var payments = await customer.GetAsync<List<PaymentResponse>>("/api/payments/mine");

        payments.Should().NotBeNull();
        payments.Should().ContainSingle();
        payments![0].OrderId.Should().Be(order!.OrderId);
        payments[0].Status.Should().Be(PaymentStatus.Pending);
        payments[0].TransactionReference.Should().StartWith("PAY-");
        payments[0].Amount.Should().Be(order.TotalAmount);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        (await context.Payments.AsNoTracking().CountAsync(p => p.OrderId == order.OrderId)).Should().Be(1);
    }

    [Fact]
    public async Task A_signed_webhook_marks_the_payment_paid_and_the_order_paid()
    {
        var (customer, _) = await NewCustomerAsync();
        var (_, order) = await PlaceOrderAsync(customer);
        var payment = (await customer.GetAsync<List<PaymentResponse>>("/api/payments/mine"))![0];

        var response = await SendWebhookAsync(SignedBody("evt-paid-1", "completed", payment.GatewayPaymentId, payment.Amount));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await ApiClient.ReadAsync<PaymentResponse>(response);
        updated!.Status.Should().Be(PaymentStatus.Succeeded);

        var reloaded = (await customer.GetAsync<List<PaymentResponse>>("/api/payments/mine"))![0];
        reloaded.Status.Should().Be(PaymentStatus.Succeeded);

        var orderResponse = await customer.GetAsync<OrderResponse>($"/api/orders/{order!.OrderId}");
        orderResponse!.Status.Should().Be(OrderStatus.Confirmed);
    }

    [Fact]
    public async Task A_webhook_with_a_bad_signature_is_rejected_and_changes_nothing()
    {
        var (customer, _) = await NewCustomerAsync();
        await PlaceOrderAsync(customer);
        var payment = (await customer.GetAsync<List<PaymentResponse>>("/api/payments/mine"))![0];

        var body = SignedBody("evt-bad-signature", "completed", payment.GatewayPaymentId, payment.Amount);
        var response = await PostWebhookAsync(body, "not-the-right-signature");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ApiClient.ReadTextAsync(response)).Should().Contain("signature");

        var unchanged = (await customer.GetAsync<List<PaymentResponse>>("/api/payments/mine"))![0];
        unchanged.Status.Should().Be(PaymentStatus.Pending, "a forged event must not move money");

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var webhook = await context.PaymentWebhooks.AsNoTracking().SingleAsync(w => w.ProviderEventId == "evt-bad-signature");
        webhook.SignatureValid.Should().BeFalse();
        webhook.IsProcessed.Should().BeFalse();
        webhook.FailureReason.Should().Be("invalid-signature");
    }

    [Fact]
    public async Task An_unsigned_webhook_is_rejected()
    {
        var (customer, _) = await NewCustomerAsync();
        await PlaceOrderAsync(customer);
        var payment = (await customer.GetAsync<List<PaymentResponse>>("/api/payments/mine"))![0];

        var response = await PostWebhookAsync(
            SignedBody("evt-no-signature", "completed", payment.GatewayPaymentId, payment.Amount),
            signature: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Replaying_the_same_provider_event_is_acknowledged_without_applying_it_twice()
    {
        var (customer, _) = await NewCustomerAsync();
        await PlaceOrderAsync(customer);
        var payment = (await customer.GetAsync<List<PaymentResponse>>("/api/payments/mine"))![0];

        var body = SignedBody("evt-replay", "completed", payment.GatewayPaymentId, payment.Amount);

        var first = await SendWebhookAsync(body);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var replay = await SendWebhookAsync(body);
        replay.StatusCode.Should().Be(HttpStatusCode.OK, "a duplicate event is acknowledged, not retried forever");

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();

        (await context.PaymentWebhooks.AsNoTracking().CountAsync(w => w.ProviderEventId == "evt-replay"))
            .Should().Be(1, "the duplicate must not be stored a second time");
    }

    [Fact]
    public async Task A_webhook_whose_amount_disagrees_with_the_payment_is_rejected()
    {
        var (customer, _) = await NewCustomerAsync();
        await PlaceOrderAsync(customer);
        var payment = (await customer.GetAsync<List<PaymentResponse>>("/api/payments/mine"))![0];

        var response = await SendWebhookAsync(
            SignedBody("evt-amount-mismatch", "completed", payment.GatewayPaymentId, payment.Amount + 50m));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var unchanged = (await customer.GetAsync<List<PaymentResponse>>("/api/payments/mine"))![0];
        unchanged.Status.Should().NotBe(PaymentStatus.Succeeded, "a mismatched amount must never settle the payment");
    }

    [Fact]
    public async Task A_failure_webhook_marks_the_payment_failed()
    {
        var (customer, _) = await NewCustomerAsync();
        await PlaceOrderAsync(customer);
        var payment = (await customer.GetAsync<List<PaymentResponse>>("/api/payments/mine"))![0];

        var response = await SendWebhookAsync(
            SignedBody("evt-failed", "payment.failed", payment.GatewayPaymentId, payment.Amount));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ApiClient.ReadAsync<PaymentResponse>(response))!.Status.Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public async Task A_customer_can_request_a_refund_for_their_own_order()
    {
        var (customer, _) = await NewCustomerAsync();
        var (_, order) = await PlaceOrderAsync(customer);

        await DeliverAsync(customer, order);

        var itemId = await FirstOrderItemIdAsync(order.OrderId);
        var response = await customer.PostAsync("/api/refunds",
            new CreateRefundRequest(order.OrderId, [itemId], "damaged", "Arrived broken"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var refund = await ApiClient.ReadAsync<RefundResponse>(response);

        refund!.Status.Should().Be(RefundStatus.Requested);
        refund.Amount.Should().BeGreaterThan(0m);
        refund.Reason.Should().Be("damaged");
    }

    [Fact]
    public async Task A_refund_request_for_someone_elses_order_is_refused()
    {
        var (customer, _) = await NewCustomerAsync();
        var (_, order) = await PlaceOrderAsync(customer);
        var (stranger, _) = await NewCustomerAsync();

        var itemId = await FirstOrderItemIdAsync(order.OrderId);
        var response = await stranger.PostAsync("/api/refunds",
            new CreateRefundRequest(order.OrderId, [itemId], "not mine", null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_admin_approves_a_refund_and_the_order_can_then_be_refunded()
    {
        var (customer, _) = await NewCustomerAsync();
        var (_, order) = await PlaceOrderAsync(customer);
        var payment = (await customer.GetAsync<List<PaymentResponse>>("/api/payments/mine"))![0];

        // The money has to be in the account before a refund means anything.
        (await SendWebhookAsync(SignedBody("evt-before-refund", "completed", payment.GatewayPaymentId, payment.Amount)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await DeliverAsync(customer, order);

        var itemId = await FirstOrderItemIdAsync(order.OrderId);
        var requested = await customer.PostAsync("/api/refunds",
            new CreateRefundRequest(order.OrderId, [itemId], "damaged", null));
        requested.StatusCode.Should().Be(HttpStatusCode.Created, await ApiClient.ReadTextAsync(requested));
        var refund = await ApiClient.ReadAsync<RefundResponse>(requested);

        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var review = await admin.PutAsync($"/api/refunds/{refund!.Id}/status",
            new ReviewRefundRequest(ReviewRefundAction.Approve, "Approved after photos"));

        review.StatusCode.Should().Be(HttpStatusCode.OK, await ApiClient.ReadTextAsync(review));
        var reviewed = await ApiClient.ReadAsync<RefundResponse>(review);

        // Approval settles against the gateway straight away, so the refund lands on Completed
        // rather than waiting in Approved for a payout run.
        reviewed!.Status.Should().Be(RefundStatus.Completed);
        reviewed.ReviewedAt.Should().NotBeNull();

        var reread = (await customer.GetAsync<List<RefundResponse>>("/api/refunds"))!
            .Single(r => r.Id == refund.Id);
        reread.Status.Should().Be(RefundStatus.Completed);
    }

    [Fact]
    public async Task A_rejected_refund_records_why_and_pays_nothing()
    {
        var (customer, _) = await NewCustomerAsync();
        var (_, order) = await PlaceOrderAsync(customer);
        var payment = (await customer.GetAsync<List<PaymentResponse>>("/api/payments/mine"))![0];
        (await SendWebhookAsync(SignedBody("evt-before-reject", "completed", payment.GatewayPaymentId, payment.Amount)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await DeliverAsync(customer, order);

        var itemId = await FirstOrderItemIdAsync(order.OrderId);
        var requested = await customer.PostAsync("/api/refunds",
            new CreateRefundRequest(order.OrderId, [itemId], "changed mind", null));
        requested.StatusCode.Should().Be(HttpStatusCode.Created, await ApiClient.ReadTextAsync(requested));
        var refund = await ApiClient.ReadAsync<RefundResponse>(requested);

        var (admin, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var review = await admin.PutAsync($"/api/refunds/{refund!.Id}/status",
            new ReviewRefundRequest(ReviewRefundAction.Reject, "Outside the return window"));

        review.StatusCode.Should().Be(HttpStatusCode.OK, await ApiClient.ReadTextAsync(review));
        var reviewed = await ApiClient.ReadAsync<RefundResponse>(review);

        reviewed!.Status.Should().Be(RefundStatus.Rejected);
        reviewed.RejectionReason.Should().Be("Outside the return window");
    }

    [Fact]
    public async Task A_customer_cannot_review_their_own_refund()
    {
        var (customer, _) = await NewCustomerAsync();
        var (_, order) = await PlaceOrderAsync(customer);
        await DeliverAsync(customer, order);

        var itemId = await FirstOrderItemIdAsync(order.OrderId);

        var requested = await customer.PostAsync("/api/refunds",
            new CreateRefundRequest(order.OrderId, [itemId], "damaged", null));
        requested.StatusCode.Should().Be(HttpStatusCode.Created, await ApiClient.ReadTextAsync(requested));
        var refund = await ApiClient.ReadAsync<RefundResponse>(requested);

        var review = await customer.PutAsync($"/api/refunds/{refund!.Id}/status",
            new ReviewRefundRequest(ReviewRefundAction.Approve, "self approved"));

        review.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<(ApiClient Client, Guid AddressId)> NewCustomerAsync()
    {
        var email = $"payments-{Guid.NewGuid():N}@test.dev";
        var (client, _) = await AuthHelper.RegisterAsync(_factory, email, MarketplaceTestData.CustomerPassword);

        var address = await client.PostAsync("/api/addresses", new CreateAddressRequest(
            "Home", "Test Customer", "+9779800000000", "12 Ratna Marg", null, "Kathmandu", null, "44600", "NP", true));

        if (!address.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"address {(int)address.StatusCode}: " + await ApiClient.ReadTextAsync(address));
        }
        return (client, (await ApiClient.ReadAsync<AddressResponse>(address))!.Id);
    }

    private async Task<(HttpResponseMessage Response, CheckoutResponse Order)> PlaceOrderAsync(ApiClient customer)
    {
        await customer.PostAsync("/api/cart/items", new AddCartItemRequest(
            _data.SellerAProductId, _data.SellerAProductVariantId, 1));

        var addresses = await customer.GetAsync<List<AddressResponse>>("/api/addresses");
        var addressId = addresses![0].Id;

        var response = await customer.PostAsync("/api/checkout",
            new CheckoutRequest(addressId, "Mock", null, null, null, null));

        return (response, (await ApiClient.ReadAsync<CheckoutResponse>(response))!);
    }

    /// <summary>Builds a webhook body and signs it the way the provider would.</summary>
    private static string SignedBody(string eventId, string eventType, string? gatewayPaymentId, decimal amount) =>
        JsonSerializer.Serialize(new
        {
            provider = "Mock",
            event_id = eventId,
            event_type = eventType,
            gateway_payment_id = gatewayPaymentId,
            amount,
            currency = "USD"
        });

    private static string Sign(string body) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), Encoding.UTF8.GetBytes(body)))
            .ToLowerInvariant();

    private Task<HttpResponseMessage> SendWebhookAsync(string body) => PostWebhookAsync(body, Sign(body));

    private Task<HttpResponseMessage> PostWebhookAsync(string body, string? signature)
    {
        var client = new ApiClient(_factory.CreateClient());
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        if (signature is not null)
        {
            request.Headers.Add("X-Signature", signature);
        }

        return client.Http.SendAsync(request);
    }

    /// <summary>
    /// Takes an order all the way to delivered: a refund is only meaningful once the customer
    /// has the goods, so the scenario has to walk the fulfilment path rather than jump there.
    /// </summary>
    private async Task DeliverAsync(ApiClient customer, CheckoutResponse order)
    {
        var payment = (await customer.GetAsync<List<PaymentResponse>>("/api/payments/mine"))![0];

        (await SendWebhookAsync(SignedBody($"evt-deliver-{Guid.NewGuid():N}", "completed", payment.GatewayPaymentId, payment.Amount)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var (seller, _) = await AuthHelper.SignInAsync(
            _factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);

        var sellerOrders = await seller.GetAsync<PagedResult<SellerOrderSummaryResponse>>("/api/seller/orders");
        var mine = sellerOrders!.Items.Where(so => so.OrderId == order.OrderId).ToList();
        mine.Should().NotBeEmpty("the seller must be able to see the order they were given");

        foreach (var sellerOrder in mine)
        {
            foreach (var step in new[]
                     {
                         SellerOrderStatus.Confirmed, SellerOrderStatus.Processing,
                         SellerOrderStatus.Packed, SellerOrderStatus.Shipped, SellerOrderStatus.Delivered
                     })
            {
                var response = await seller.PutAsync(
                    $"/api/seller/orders/{sellerOrder.Id}/status",
                    new UpdateOrderStatusRequest(step, "integration test fulfilment", "DHL", "TRACK-1", null, null));

                response.StatusCode.Should().Be(HttpStatusCode.NoContent, $"moving the sub-order to {step}");
            }
        }

        var delivered = await customer.GetAsync<OrderResponse>($"/api/orders/{order.OrderId}");
        delivered!.Status.Should().Be(OrderStatus.Delivered, "the parent order must follow the sub-order");
    }
    private async Task<Guid> FirstOrderItemIdAsync(Guid orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        return await context.OrderItems.AsNoTracking()
            .Where(i => i.OrderId == orderId)
            .Select(i => i.Id)
            .FirstAsync();
    }
}
