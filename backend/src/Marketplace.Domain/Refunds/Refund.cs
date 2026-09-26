using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Refunds;

/// <summary>
/// A refund request raised by a customer and reviewed by an admin. The lifecycle
/// (Requested → Approved → Completed) is guarded so a refund can never be paid twice.
/// </summary>
public class Refund : Entity
{
    private readonly List<RefundItem> _items = [];

    private Refund()
    {
        Reason = string.Empty;
    }

    private Refund(Guid id, Guid orderId, Guid paymentId, Guid customerId, Guid sellerId, decimal amount, string reason, string? description, DateTimeOffset now)
        : base(id)
    {
        OrderId = orderId;
        PaymentId = paymentId;
        CustomerId = customerId;
        SellerId = sellerId;
        Amount = amount;
        Reason = reason;
        Description = description?.Trim();
        Status = RefundStatus.Requested;
        RequestedAt = now;
        CreatedAt = now;
    }

    public Guid OrderId { get; private set; }

    public Guid PaymentId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid SellerId { get; private set; }

    public RefundStatus Status { get; private set; }

    public decimal Amount { get; private set; }

    public string Reason { get; private set; }

    public string? Description { get; private set; }

    public string? ReviewNote { get; private set; }

    public string? RejectionReason { get; private set; }

    public Guid? ReviewedByUserId { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? GatewayRefundId { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<RefundItem> Items => _items.AsReadOnly();

    public bool IsFinal => Status is RefundStatus.Completed or RefundStatus.Rejected or RefundStatus.Cancelled;

    public static Refund Create(Guid orderId, Guid paymentId, Guid customerId, Guid sellerId, decimal amount, string reason, string? description, DateTimeOffset now)
    {
        Guard.NotEmpty(orderId, nameof(orderId));
        Guard.NotEmpty(paymentId, nameof(paymentId));
        Guard.NotEmpty(customerId, nameof(customerId));
        Guard.NotEmpty(sellerId, nameof(sellerId));
        Guard.InRange(amount, 0.01m, 10_000_000m, nameof(amount));
        Guard.NotNullOrWhiteSpace(reason, nameof(reason));

        return new Refund(SequentialGuid.New(now), orderId, paymentId, customerId, sellerId, decimal.Round(amount, 2), reason.Trim(), description, now);
    }

    public void AddItem(Guid orderItemId, Guid productId, string productName, string sku, int quantity, decimal amount, DateTimeOffset now)
    {
        Guard.GreaterThanZero(quantity, nameof(quantity));

        if (_items.Any(i => i.OrderItemId == orderItemId))
        {
            throw new DuplicateEntityException(nameof(RefundItem), nameof(orderItemId), orderItemId.ToString());
        }

        _items.Add(RefundItem.Create(Id, orderItemId, productId, productName, sku, quantity, amount, now));
    }

    public void StartReview(Guid reviewerUserId, DateTimeOffset now)
    {
        EnsureIn(RefundStatus.Requested);
        Status = RefundStatus.UnderReview;
        ReviewedByUserId = reviewerUserId;
        UpdatedAt = now;
    }

    public void Approve(Guid reviewerUserId, string? note, DateTimeOffset now)
    {
        EnsureIn(RefundStatus.Requested, RefundStatus.UnderReview);

        Status = RefundStatus.Approved;
        ReviewedByUserId = reviewerUserId;
        ReviewNote = note?.Trim();
        ReviewedAt = now;
        UpdatedAt = now;
    }

    public void Reject(Guid reviewerUserId, string reason, DateTimeOffset now)
    {
        EnsureIn(RefundStatus.Requested, RefundStatus.UnderReview);
        Guard.NotNullOrWhiteSpace(reason, nameof(reason));

        Status = RefundStatus.Rejected;
        ReviewedByUserId = reviewerUserId;
        RejectionReason = reason.Trim();
        ReviewedAt = now;
        UpdatedAt = now;
    }

    public void MarkProcessing(DateTimeOffset now)
    {
        EnsureIn(RefundStatus.Approved);
        Status = RefundStatus.Processing;
        UpdatedAt = now;
    }

    public void MarkCompleted(string gatewayRefundId, DateTimeOffset now)
    {
        EnsureIn(RefundStatus.Approved, RefundStatus.Processing);

        Status = RefundStatus.Completed;
        GatewayRefundId = gatewayRefundId;
        CompletedAt = now;
        UpdatedAt = now;
    }

    public void MarkFailed(string reason, DateTimeOffset now)
    {
        EnsureIn(RefundStatus.Approved, RefundStatus.Processing);

        Status = RefundStatus.Failed;
        FailureReason = reason;
        UpdatedAt = now;
    }

    public void Cancel(string reason, DateTimeOffset now)
    {
        EnsureIn(RefundStatus.Requested);
        Status = RefundStatus.Cancelled;
        RejectionReason = reason;
        UpdatedAt = now;
    }

    private void EnsureIn(params RefundStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new InvalidStateTransitionException(nameof(Refund), Status.ToString(), string.Join("/", allowed.Select(a => a.ToString())));
        }
    }
}

public class RefundItem : Entity
{
    private RefundItem()
    {
        Sku = string.Empty;
        ProductName = string.Empty;
    }

    private RefundItem(Guid id, Guid refundId, Guid orderItemId, Guid productId, string productName, string sku, int quantity, decimal amount, DateTimeOffset now)
        : base(id)
    {
        RefundId = refundId;
        OrderItemId = orderItemId;
        ProductId = productId;
        ProductName = productName;
        Sku = sku;
        Quantity = quantity;
        Amount = amount;
        CreatedAt = now;
    }

    public Guid RefundId { get; private set; }

    /// <summary>Enforces the "one refund request per order item" rule through a unique index.</summary>
    public Guid OrderItemId { get; private set; }

    public Guid ProductId { get; private set; }

    public string ProductName { get; private set; }

    public string Sku { get; private set; }

    public int Quantity { get; private set; }

    public decimal Amount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static RefundItem Create(Guid refundId, Guid orderItemId, Guid productId, string productName, string sku, int quantity, decimal amount, DateTimeOffset now)
    {
        Guard.NotEmpty(refundId, nameof(refundId));
        Guard.NotEmpty(orderItemId, nameof(orderItemId));
        Guard.GreaterThanZero(quantity, nameof(quantity));

        return new RefundItem(SequentialGuid.New(now), refundId, orderItemId, productId, productName?.Trim() ?? string.Empty, sku?.Trim() ?? string.Empty, quantity, decimal.Round(amount, 2), now);
    }
}
