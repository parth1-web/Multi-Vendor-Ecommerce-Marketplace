using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Payments;

/// <summary>Immutable record of one interaction with a payment gateway.</summary>
public class PaymentTransaction : Entity
{
    private PaymentTransaction() => Type = PaymentTransactionType.GatewayRequest;

    private PaymentTransaction(Guid id, Guid paymentId, PaymentTransactionType type, string? requestPayload, string? responsePayload, bool isSuccess, string? errorMessage, DateTimeOffset now)
        : base(id)
    {
        PaymentId = paymentId;
        Type = type;
        RequestPayload = requestPayload;
        ResponsePayload = responsePayload;
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        CreatedAt = now;
    }

    public Guid PaymentId { get; private set; }

    public PaymentTransactionType Type { get; private set; }

    public string? RequestPayload { get; private set; }

    public string? ResponsePayload { get; private set; }

    public bool IsSuccess { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static PaymentTransaction Create(
        Guid paymentId,
        PaymentTransactionType type,
        string? requestPayload,
        string? responsePayload,
        bool isSuccess,
        string? errorMessage,
        DateTimeOffset now)
    {
        Guard.NotEmpty(paymentId, nameof(paymentId));

        return new PaymentTransaction(SequentialGuid.New(now), paymentId, type, requestPayload, responsePayload, isSuccess, errorMessage, now);
    }
}

/// <summary>
/// A raw callback received from a gateway. Persisted before processing so an
/// investigation can always replay exactly what arrived.
/// </summary>
public class PaymentWebhook : Entity
{
    private PaymentWebhook()
    {
        Provider = string.Empty;
        ProviderEventId = string.Empty;
        Payload = string.Empty;
    }

    private PaymentWebhook(Guid id, Guid? paymentId, string provider, string providerEventId, string? signature, string payload, string? eventType, DateTimeOffset now)
        : base(id)
    {
        PaymentId = paymentId;
        Provider = provider;
        ProviderEventId = providerEventId;
        Signature = signature;
        Payload = payload;
        EventType = eventType;
        ReceivedAt = now;
    }

    public Guid? PaymentId { get; private set; }

    public string Provider { get; private set; }

    /// <summary>Provider's unique event id — the idempotency key for the whole callback.</summary>
    public string ProviderEventId { get; private set; }

    public string? Signature { get; private set; }

    public string Payload { get; private set; }

    public string? EventType { get; private set; }

    public bool IsProcessed { get; private set; }

    public bool SignatureValid { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public static PaymentWebhook Create(Guid? paymentId, string provider, string providerEventId, string? signature, string payload, string? eventType, DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(provider, nameof(provider));
        Guard.NotNullOrWhiteSpace(providerEventId, nameof(providerEventId));
        Guard.NotNullOrWhiteSpace(payload, nameof(payload));

        return new PaymentWebhook(SequentialGuid.New(now), paymentId, provider.Trim(), providerEventId.Trim(), signature, payload, eventType, now);
    }

    public void MarkSignatureValid(DateTimeOffset now)
    {
        SignatureValid = true;
        FailureReason = null;
        _ = now;
    }

    public void MarkProcessed(Guid paymentId, DateTimeOffset now)
    {
        PaymentId = paymentId;
        IsProcessed = true;
        ProcessedAt = now;
    }

    public void MarkFailed(string reason, DateTimeOffset now)
    {
        FailureReason = reason;
        _ = now;
    }
}
