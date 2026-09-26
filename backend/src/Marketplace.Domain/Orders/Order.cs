using System.Security.Cryptography;
using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Identity;

namespace Marketplace.Domain.Orders;

/// <summary>
/// The customer-facing marketplace order. One customer checkout always produces exactly
/// one of these, regardless of how many sellers are involved; the per-seller work is
/// carried by the <see cref="SellerOrder"/> children.
/// </summary>
public class Order : Entity
{
    private readonly List<SellerOrder> _sellerOrders = [];
    private readonly List<OrderItem> _items = [];
    private readonly List<OrderStatusHistory> _history = [];

    private Order()
    {
        OrderNumber = string.Empty;
        Currency = "USD";
        PaymentMethod = string.Empty;
    }

    private Order(
        Guid id,
        string orderNumber,
        Guid customerId,
        OrderAddressSnapshot shippingAddress,
        Guid? couponId,
        string? couponCode,
        string? idempotencyKey,
        decimal subtotal,
        decimal discountAmount,
        decimal shippingAmount,
        decimal taxAmount,
        string currency,
        string paymentMethod,
        string? customerNote,
        DateTimeOffset now)
        : base(id)
    {
        OrderNumber = orderNumber;
        CustomerId = customerId;
        ShippingAddressSnapshot = shippingAddress;
        CouponId = couponId;
        CouponCode = couponCode;
        IdempotencyKey = idempotencyKey;

        Subtotal = subtotal;
        DiscountAmount = discountAmount;
        ShippingAmount = shippingAmount;
        TaxAmount = taxAmount;
        TotalAmount = subtotal - discountAmount + shippingAmount + taxAmount;
        Currency = currency;
        PaymentMethod = paymentMethod;
        CustomerNote = customerNote;
        Status = OrderStatus.Pending;
        PlacedAt = now;
        CreatedAt = now;
    }

    /// <summary>Human-facing, sortable, non-enumerable order number, e.g. <c>MP-20260226-7F3A9C21</c>.</summary>
    public string OrderNumber { get; private set; }

    public Guid CustomerId { get; private set; }

    public OrderStatus Status { get; private set; }

    public decimal Subtotal { get; private set; }

    public decimal DiscountAmount { get; private set; }

    public decimal ShippingAmount { get; private set; }

    public decimal TaxAmount { get; private set; }

    public decimal TotalAmount { get; private set; }

    /// <summary>Refunded so far; capped at the order total.</summary>
    public decimal RefundedAmount { get; private set; }

    public string Currency { get; private set; }

    public string PaymentMethod { get; private set; }

    public bool IsPaid { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public Guid? CouponId { get; private set; }

    public string? CouponCode { get; private set; }

    /// <summary>
    /// Client-supplied key that makes a retried checkout return the order it already created.
    /// It is stored on its own column: reusing the coupon column would let a real coupon code
    /// match someone else's retry, and would report a discount the order never had.
    /// </summary>
    public string? IdempotencyKey { get; private set; }

    public OrderAddressSnapshot ShippingAddressSnapshot { get; private set; } = new();

    public string? CustomerNote { get; private set; }

    public DateTimeOffset PlacedAt { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public DateTimeOffset? ShippedAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<SellerOrder> SellerOrders => _sellerOrders.AsReadOnly();

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    public IReadOnlyCollection<OrderStatusHistory> History => _history.AsReadOnly();

    public bool IsCancelled => Status == OrderStatus.Cancelled;

    public bool IsRefundable => RefundedAmount < TotalAmount;

    public decimal RefundableAmount => decimal.Round(TotalAmount - RefundedAmount, 2, MidpointRounding.AwayFromZero);

    public static Order Place(
        Guid customerId,
        OrderAddressSnapshot shippingAddress,
        Guid? couponId,
        string? couponCode,
        string? idempotencyKey,
        decimal subtotal,
        decimal discountAmount,
        decimal shippingAmount,
        decimal taxAmount,
        string currency,
        string paymentMethod,
        string? customerNote,
        DateTimeOffset now)
    {
        Guard.NotEmpty(customerId, nameof(customerId));
        ArgumentNullException.ThrowIfNull(shippingAddress);

        Guard.GreaterThanOrEqualToZero(subtotal, nameof(subtotal));
        Guard.GreaterThanOrEqualToZero(discountAmount, nameof(discountAmount));
        Guard.GreaterThanOrEqualToZero(shippingAmount, nameof(shippingAmount));
        Guard.GreaterThanOrEqualToZero(taxAmount, nameof(taxAmount));

        if (discountAmount > subtotal)
        {
            throw new ValidationException(nameof(discountAmount), "The discount cannot exceed the subtotal.");
        }

        var total = decimal.Round(subtotal - discountAmount + shippingAmount + taxAmount, 2, MidpointRounding.AwayFromZero);
        if (total <= 0m)
        {
            throw new ValidationException(nameof(subtotal), "The order total must be greater than zero.");
        }

        return new Order(
            SequentialGuid.New(now),
            GenerateOrderNumber(now),
            customerId,
            shippingAddress,
            couponId,
            couponCode?.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(),
            decimal.Round(subtotal, 2),

            decimal.Round(discountAmount, 2),
            decimal.Round(shippingAmount, 2),
            decimal.Round(taxAmount, 2),
            string.IsNullOrWhiteSpace(currency) ? "USD" : currency.ToUpperInvariant(),
            paymentMethod,
            customerNote?.Trim(),
            now);
    }

    public void AddSellerOrder(SellerOrder sellerOrder)
    {
        ArgumentNullException.ThrowIfNull(sellerOrder);
        _sellerOrders.Add(sellerOrder);
    }

    public void AddItem(OrderItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
    }

    public void RecordStatusChange(OrderStatus from, OrderStatus to, string? note, Guid? changedByUserId, DateTimeOffset now)
    {
        OrderStatusTransition.EnsureCanTransition(from, to);
        Status = to;
        UpdatedAt = now;

        _history.Add(OrderStatusHistory.Create(Id, OrderNumber, from, to, note, changedByUserId, now));

        switch (to)
        {
            case OrderStatus.Confirmed:
                ConfirmedAt ??= now;
                break;
            case OrderStatus.Shipped:
                ShippedAt ??= now;
                break;
            case OrderStatus.Delivered:
                DeliveredAt ??= now;
                break;
            case OrderStatus.Cancelled:
                CancelledAt ??= now;
                break;
        }
    }

    public void ChangeStatus(OrderStatus to, string? note, Guid? changedByUserId, DateTimeOffset now) =>
        RecordStatusChange(Status, to, note, changedByUserId, now);

    public void Cancel(string reason, Guid? cancelledByUserId, DateTimeOffset now)
    {
        if (!OrderStatusTransition.IsCancellableByCustomer(Status))
        {
            throw new InvalidStateTransitionException("Order", Status.ToString(), OrderStatus.Cancelled.ToString());
        }

        RecordStatusChange(Status, OrderStatus.Cancelled, reason, cancelledByUserId, now);
        CancellationReason = reason?.Trim();

        foreach (var sellerOrder in _sellerOrders)
        {
            sellerOrder.Cancel(reason ?? "Order cancelled", cancelledByUserId, now);
        }
    }

    public void MarkPaid(DateTimeOffset now)
    {
        if (IsPaid)
        {
            return;
        }

        IsPaid = true;
        PaidAt = now;

        // A paid order is a confirmed order. Leaving the status at Pending makes the customer's
        // own history read as though payment were still outstanding, and leaves the seller with
        // nothing to fulfil against.
        if (Status == OrderStatus.Pending)
        {
            RecordStatusChange(OrderStatus.Pending, OrderStatus.Confirmed, "Payment received", null, now);
        }

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
            throw new BusinessRuleException($"Refund of {amount:0.00} exceeds the refundable balance of {RefundableAmount:0.00}.");
        }

        RefundedAmount = decimal.Round(RefundedAmount + amount, 2, MidpointRounding.AwayFromZero);
        UpdatedAt = now;
    }

    /// <summary>
    /// Propagates a sub-order status change to the marketplace order, walking the order
    /// forward one legal step at a time so the parent never skips a state.
    ///
    /// The parent deliberately stops at <see cref="OrderStatus.Delivered"/>: only the
    /// "every sub-order has completed" rule may move it to Completed, otherwise one fast
    /// seller would complete the whole marketplace order.
    /// </summary>
    public void SyncFromSellerOrder(SellerOrderStatus sellerStatus, DateTimeOffset now)
    {
        var mapped = (OrderStatus)Math.Min((int)sellerStatus, (int)OrderStatus.Delivered);

        while (Status != mapped)
        {
            var candidates = OrderStatusTransition.NextStates(Status)
                .Where(s => (int)s <= (int)mapped)
                .OrderBy(s => (int)mapped - (int)s)
                .ToList();

            if (candidates.Count == 0)
            {
                break;
            }

            RecordStatusChange(Status, candidates[0], "Synchronised from seller fulfilment", null, now);
        }

        if (Status == mapped)
        {
            UpdatedAt = now;
        }

        // The marketplace order completes only once every sub-order has completed.
        if (_sellerOrders.Count > 0 && _sellerOrders.All(s => s.Status == SellerOrderStatus.Completed) && Status == OrderStatus.Delivered)
        {
            Status = OrderStatus.Completed;
            UpdatedAt = now;
        }
    }

    /// <summary>
    /// Builds the customer-facing order number, e.g. <c>MP-20260226-7F3A9C21B4D0</c>.
    /// </summary>
    /// <remarks>
    /// The random part has to be genuinely random. Deriving it from the order's sequential id
    /// looks attractive because that id is already unique, but a sequential id leads with its
    /// own timestamp, so two orders placed in the same tick come out with the same number and
    /// the unique index rejects the second one. 48 bits of entropy makes that unreachable in
    /// practice while keeping the number short enough to read out over the phone.
    /// </remarks>
    public static string GenerateOrderNumber(DateTimeOffset now) =>
        $"MP-{now:yyyyMMdd}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(6))}";
}

