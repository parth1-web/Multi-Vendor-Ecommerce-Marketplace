using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Payments;

/// <summary>
/// A payment attempt against an order. Only a verified server-side event (gateway
/// verification or a signed webhook) can move this to <see cref="PaymentStatus.Succeeded"/>;
/// a browser success callback never can.
/// </summary>
public class Payment : Entity
{
    private readonly List<PaymentTransaction> _transactions = [];

    private Payment()
    {
        TransactionReference = string.Empty;
        FailureReason = string.Empty;
        Currency = "USD";
    }

    private Payment(Guid id, Guid orderId, Guid customerId, PaymentProvider provider, decimal amount, string currency, string transactionReference, string? idempotencyKey, DateTimeOffset now)
        : base(id)
    {
        OrderId = orderId;
        CustomerId = customerId;
        Provider = provider;
        Amount = amount;
        Currency = currency;
        TransactionReference = transactionReference;
        IdempotencyKey = idempotencyKey;
        Status = PaymentStatus.Initiated;
        InitiatedAt = now;
        CreatedAt = now;
    }

    public Guid OrderId { get; private set; }

    public Guid CustomerId { get; private set; }

    public PaymentProvider Provider { get; private set; }

    public PaymentStatus Status { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public string TransactionReference { get; private set; }

    /// <summary>Client-supplied key that makes payment creation safely retryable.</summary>
    public string? IdempotencyKey { get; private set; }

    public string? GatewayPaymentId { get; private set; }

    public string? GatewayRedirectUrl { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset InitiatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public decimal RefundedAmount { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<PaymentTransaction> Transactions => _transactions.AsReadOnly();

    public bool IsSettled => Status is PaymentStatus.Succeeded or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded;

    public bool IsCashOnDelivery => Provider == PaymentProvider.CashOnDelivery;

    public decimal RefundableAmount => decimal.Round(Amount - RefundedAmount, 2, MidpointRounding.AwayFromZero);

    public static Payment Create(
        Guid orderId,
        Guid customerId,
        PaymentProvider provider,
        decimal amount,
        string currency,
        string? idempotencyKey,
        DateTimeOffset now)
    {
        Guard.NotEmpty(orderId, nameof(orderId));
        Guard.NotEmpty(customerId, nameof(customerId));
        Guard.InRange(amount, 0.01m, 10_000_000m, nameof(amount));

        return new Payment(
            SequentialGuid.New(now),
            orderId,
            customerId,
            provider,
            decimal.Round(amount, 2),
            string.IsNullOrWhiteSpace(currency) ? "USD" : currency.ToUpperInvariant(),
            GenerateReference(now),
            idempotencyKey,
            now);
    }

    public void AttachGatewayResponse(string? gatewayPaymentId, string? redirectUrl, PaymentTransaction response, DateTimeOffset now)
    {
        GatewayPaymentId = gatewayPaymentId;
        GatewayRedirectUrl = redirectUrl;
        _transactions.Add(response);

        if (Status == PaymentStatus.Initiated)
        {
            Status = redirectUrl is null ? PaymentStatus.Pending : PaymentStatus.Pending;
        }

        UpdatedAt = now;
    }

    public void RecordTransaction(PaymentTransaction transaction, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        _transactions.Add(transaction);
        UpdatedAt = now;
    }

    /// <summary>
    /// Marks the payment settled. Uses a conditional status check in the service layer so a
    /// duplicated webhook cannot capture twice.
    /// </summary>
    public void MarkSucceeded(string? gatewayPaymentId, PaymentTransaction verification, DateTimeOffset now)
    {
        if (Status is PaymentStatus.Succeeded or PaymentStatus.Refunded)
        {
            return;
        }

        if (Status is PaymentStatus.Cancelled or PaymentStatus.Refunded)
        {
            throw new InvalidStateTransitionException(nameof(Payment), Status.ToString(), PaymentStatus.Succeeded.ToString());
        }

        Status = PaymentStatus.Succeeded;
        GatewayPaymentId = gatewayPaymentId ?? GatewayPaymentId;
        CompletedAt = now;
        UpdatedAt = now;
        FailureReason = null;
        _transactions.Add(verification);
    }

    public void MarkFailed(string reason, PaymentTransaction transaction, DateTimeOffset now)
    {
        if (Status == PaymentStatus.Succeeded)
        {
            return;
        }

        Status = PaymentStatus.Failed;
        FailureReason = reason;
        CompletedAt = now;
        UpdatedAt = now;
        _transactions.Add(transaction);
    }

    public void MarkCancelled(string reason, DateTimeOffset now)
    {
        if (Status is PaymentStatus.Succeeded or PaymentStatus.Refunded)
        {
            throw new InvalidStateTransitionException(nameof(Payment), Status.ToString(), PaymentStatus.Cancelled.ToString());
        }

        Status = PaymentStatus.Cancelled;
        FailureReason = reason;
        UpdatedAt = now;
    }

    public void RecordRefund(decimal amount, DateTimeOffset now)
    {
        if (amount <= 0m)
        {
            throw new ValidationException(nameof(amount), "Refund amount must be greater than zero.");
        }

        if (amount > RefundableAmount)
        {
            throw new BusinessRuleException($"Refund of {amount:0.00} exceeds the refundable amount of {RefundableAmount:0.00}.");
        }

        RefundedAmount = decimal.Round(RefundedAmount + amount, 2, MidpointRounding.AwayFromZero);
        Status = RefundedAmount >= Amount ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        UpdatedAt = now;
    }

    public static string GenerateReference(DateTimeOffset now) =>
        $"PAY-{now:yyyyMMddHHmmss}-{Convert.ToHexString(SequentialGuid.New(now).ToByteArray())[..6]}";
}
