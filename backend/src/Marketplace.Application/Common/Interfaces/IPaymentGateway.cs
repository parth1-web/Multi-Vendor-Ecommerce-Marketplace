using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Marketplace.Application.Common.Interfaces;

/// <summary>
/// Abstraction over a payment provider. Every implementation (mock, cash-on-delivery,
/// Khalti, eSewa, Stripe) is interchangeable, which is what keeps the checkout service
/// free of provider knowledge.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Provider identifier, matching the routing key in configuration.</summary>
    string Name { get; }

    /// <summary>True when the provider requires a customer redirect or QR scan before settlement.</summary>
    bool RequiresCustomerAction { get; }

    Task<PaymentInitiationResult> CreatePaymentAsync(PaymentInitiationRequest request, CancellationToken cancellationToken = default);

    Task<PaymentVerificationResult> VerifyAsync(PaymentVerificationRequest request, CancellationToken cancellationToken = default);

    Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default);

    /// <summary>Validates a webhook signature over the raw body using a constant-time comparison.</summary>
    bool VerifySignature(string rawBody, string? signature, string secret);
}

/// <summary>Everything a gateway needs to start a payment.</summary>
public sealed record PaymentInitiationRequest(
    Guid PaymentId,
    Guid OrderId,
    string OrderNumber,
    decimal Amount,
    string Currency,
    string CustomerEmail,
    string CustomerName,
    string SuccessUrl,
    string FailUrl,
    string CancelUrl,
    string? CallbackUrl);

/// <summary>Gateway response to a payment initiation.</summary>
public sealed record PaymentInitiationResult(
    bool IsSuccess,
    string? GatewayPaymentId,
    string? RedirectUrl,
    string? QrCodeData,
    string? FailureReason,
    string? RawResponse);

/// <summary>Verification request — a server-to-server lookup, never a browser callback.</summary>
public sealed record PaymentVerificationRequest(
    Guid PaymentId,
    string? GatewayPaymentId,
    decimal ExpectedAmount,
    string Currency);

/// <summary>Gateway verification response.</summary>
public sealed record PaymentVerificationResult(
    bool IsVerified,
    decimal? Amount,
    string Currency,
    string? GatewayReference,
    string? FailureReason,
    string? RawResponse);

/// <summary>Refund request handed to the gateway.</summary>
public sealed record RefundRequest(Guid PaymentId, Guid RefundId, string GatewayPaymentId, decimal Amount, string Currency, string Reason);

/// <summary>Gateway refund response.</summary>
public sealed record RefundResult(bool IsSuccess, string? GatewayRefundId, string? FailureReason, string? RawResponse);

/// <summary>In-memory mock gateway used in development, tests and demos.</summary>
public sealed class MockPaymentGateway : IPaymentGateway
{
    public string Name => "Mock";

    public bool RequiresCustomerAction => true;

    /// <summary>
    /// Sends the customer to a page of our own rather than to a domain that does not exist.
    /// </summary>
    /// <remarks>
    /// A gateway that needs something from the customer has to be able to send them somewhere to
    /// do it, and this one is emulating a redirect provider, so it behaves like one: the payment
    /// is initiated here and settled when it is confirmed, exactly as it is for a real provider
    /// and as the webhook and verification paths expect. The difference is where that somewhere
    /// is. It used to point at a host that does not resolve, which left every order in a demo
    /// unpaid and sent the browser off to a page it could not come back from.
    /// </remarks>
    public Task<PaymentInitiationResult> CreatePaymentAsync(PaymentInitiationRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PaymentInitiationResult(
            IsSuccess: true,
            GatewayPaymentId: $"mock-{request.OrderNumber}",
            RedirectUrl: $"{request.SuccessUrl}",
            QrCodeData: null,
            FailureReason: null,
            RawResponse: $"{{\"status\":\"initiated\",\"order\":\"{request.OrderNumber}\"}}"));

    public Task<PaymentVerificationResult> VerifyAsync(PaymentVerificationRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PaymentVerificationResult(
            IsVerified: true,
            Amount: request.ExpectedAmount,
            Currency: request.Currency,
            GatewayReference: request.GatewayPaymentId ?? $"mock-{request.PaymentId:N}",
            FailureReason: null,
            RawResponse: "{\"status\":\"verified\"}"));

    public Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RefundResult(
            IsSuccess: true,
            GatewayRefundId: $"mock-refund-{request.RefundId:N}",
            FailureReason: null,
            RawResponse: "{\"status\":\"refunded\"}"));

    public bool VerifySignature(string rawBody, string? signature, string secret)
    {
        if (string.IsNullOrEmpty(signature))
        {
            return false;
        }

        var expected = Sign(rawBody, secret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));
    }

    internal static string Sign(string rawBody, string secret) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();
}

/// <summary>Cash on delivery: no gateway call, settled when the courier hands the parcel over.</summary>
public sealed class CashOnDeliveryGateway : IPaymentGateway
{
    public string Name => "CashOnDelivery";

    public bool RequiresCustomerAction => false;

    public Task<PaymentInitiationResult> CreatePaymentAsync(PaymentInitiationRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PaymentInitiationResult(
            IsSuccess: true,
            GatewayPaymentId: $"cod-{request.OrderId:N}",
            RedirectUrl: null,
            QrCodeData: null,
            FailureReason: null,
            RawResponse: "{\"status\":\"pay_on_delivery\"}"));

    public Task<PaymentVerificationResult> VerifyAsync(PaymentVerificationRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PaymentVerificationResult(
            IsVerified: true,
            Amount: request.ExpectedAmount,
            Currency: request.Currency,
            GatewayReference: request.GatewayPaymentId,
            FailureReason: null,
            RawResponse: "{\"status\":\"pay_on_delivery\"}"));

    public Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RefundResult(true, $"cod-refund-{request.RefundId:N}", null, "{\"status\":\"refunded\"}"));

    public bool VerifySignature(string rawBody, string? signature, string secret) => true;
}

/// <summary>HMAC-SHA256 signature helper shared by every webhook-capable gateway.</summary>
public static class WebhookSignature
{
    public static string ComputeHmacSha256Hex(string rawBody, string secret)
    {
        ArgumentNullException.ThrowIfNull(rawBody);
        ArgumentNullException.ThrowIfNull(secret);
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(rawBody)))
            .ToLowerInvariant();
    }

    public static bool Verify(string rawBody, string? signature, string secret, IReadOnlyDictionary<string, string>? headers = null)
    {
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        // Accept both the raw hex signature and a `sha256=<hex>` prefixed form.
        var candidate = signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? signature[7..]
            : signature;

        var expected = ComputeHmacSha256Hex(rawBody, secret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(candidate));
    }
}

/// <summary>Normalises a client IP address for audit records.</summary>
public static class IpAddressParser
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return IPAddress.TryParse(value, out var parsed) ? parsed.ToString() : value.Trim();
    }
}
