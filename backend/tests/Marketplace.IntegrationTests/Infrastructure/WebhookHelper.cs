using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Marketplace.Application.Modules.Payments.DTOs;

namespace Marketplace.IntegrationTests.Infrastructure;

/// <summary>
/// Signs and posts provider callbacks. The signature is the whole point of the endpoint, so it
/// is built here rather than being passed in by a test: a test must not be able to "verify" a
/// webhook by handing the service the answer.
/// </summary>
public static class WebhookHelper
{
    public const string Secret = "integration-webhook-secret";

    /// <summary>Builds a webhook body the way a provider would send it.</summary>
    public static string Body(string eventId, string eventType, string? gatewayPaymentId, decimal amount) =>
        JsonSerializer.Serialize(new
        {
            provider = "Mock",
            event_id = eventId,
            event_type = eventType,
            gateway_payment_id = gatewayPaymentId,
            amount,
            currency = "USD"
        });

    /// <summary>HMAC-SHA256 of the body, hex encoded, as the provider would send it.</summary>
    public static string Sign(string body) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(body)))
            .ToLowerInvariant();

    /// <summary>Posts a webhook, signed when a signature is supplied.</summary>
    public static Task<HttpResponseMessage> PostAsync(
        MarketplaceApiFactory factory, string body, string? signature)
    {
        var client = new ApiClient(factory.CreateClient());
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

    /// <summary>Posts a correctly signed webhook.</summary>
    public static Task<HttpResponseMessage> PostSignedAsync(MarketplaceApiFactory factory, string body) =>
        PostAsync(factory, body, Sign(body));

    /// <summary>
    /// Settles the payment for an order the way a provider would, so a scenario can move on to
    /// whatever it is actually about.
    /// </summary>
    public static async Task SettlePaymentAsync(
        MarketplaceApiFactory factory, ApiClient customer, Guid orderId, string eventPrefix)
    {
        var payment = (await customer.GetAsync<List<PaymentResponse>>("/api/payments/mine"))!
            .First(p => p.OrderId == orderId);

        var response = await PostSignedAsync(
            factory, Body($"{eventPrefix}-{Guid.NewGuid():N}", "completed", payment.GatewayPaymentId, payment.Amount));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK,
            $"settling the payment for order {orderId}");
    }
}
