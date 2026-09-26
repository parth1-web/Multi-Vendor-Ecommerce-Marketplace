using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Orders;

/// <summary>
/// The per-seller slice of a marketplace order. A seller can only ever load, update or
/// cancel their own sub-orders â€” the seller id lives on the row, not in a filter that
/// could be forgotten.
/// </summary>
public class SellerOrder : Entity
{
    private readonly List<OrderItem> _items = [];
    private readonly List<SellerOrderStatusHistory> _history = [];

    private SellerOrder()
    {
        SellerOrderNumber = string.Empty;
    }

    private SellerOrder(
        Guid id,
        Guid orderId,
        Guid sellerId,
        string sellerOrderNumber,
        decimal subtotal,
        decimal discountAmount,
        decimal shippingAmount,
        decimal taxAmount,
        decimal commissionRate,
        DateTimeOffset now)
        : base(id)
    {
        OrderId = orderId;
        SellerId = sellerId;
        SellerOrderNumber = sellerOrderNumber;
        Subtotal = subtotal;
        DiscountAmount = discountAmount;
        ShippingAmount = shippingAmount;
        TaxAmount = taxAmount;
        TotalAmount = decimal.Round(subtotal - discountAmount + shippingAmount + taxAmount, 2, MidpointRounding.AwayFromZero);

        // The rate is snapshotted so a later rate change cannot rewrite history.
        CommissionRate = commissionRate;
        CommissionAmount = decimal.Round((subtotal - discountAmount) * commissionRate / 100m, 2, MidpointRounding.AwayFromZero);
        SellerEarnings = decimal.Round(TotalAmount - CommissionAmount, 2, MidpointRounding.AwayFromZero);

        Status = SellerOrderStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid OrderId { get; private set; }

    /// <summary>Owning marketplace order. Navigation only — set by EF Core.</summary>
    public Order? Order { get; internal set; }

    public Guid SellerId { get; private set; }

    public string SellerOrderNumber { get; private set; }

    public SellerOrderStatus Status { get; private set; }

    public decimal Subtotal { get; private set; }

    public decimal DiscountAmount { get; private set; }

    public decimal ShippingAmount { get; private set; }

    public decimal TaxAmount { get; private set; }

    public decimal TotalAmount { get; private set; }

    /// <summary>Rate in force at checkout, as a historical snapshot.</summary>
    public decimal CommissionRate { get; private set; }

    public decimal CommissionAmount { get; private set; }

    public decimal SellerEarnings { get; private set; }

    public string? CarrierName { get; private set; }

    public string? TrackingNumber { get; private set; }

    public string? TrackingUrl { get; private set; }

    public DateTimeOffset? EstimatedDeliveryAt { get; private set; }

    public DateTimeOffset? ShippedAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    public string? SellerNote { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    public IReadOnlyCollection<SellerOrderStatusHistory> History => _history.AsReadOnly();

    public static SellerOrder Create(
        Guid orderId,
        Guid sellerId,
        string sellerOrderNumber,
        decimal subtotal,
        decimal discountAmount,
        decimal shippingAmount,
        decimal taxAmount,
        decimal commissionRate,
        DateTimeOffset now)
    {
        Guard.NotEmpty(orderId, nameof(orderId));
        Guard.NotEmpty(sellerId, nameof(sellerId));
        Guard.NotNullOrWhiteSpace(sellerOrderNumber, nameof(sellerOrderNumber));
        Guard.GreaterThanOrEqualToZero(subtotal, nameof(subtotal));
        Guard.InRange(commissionRate, 0m, 100m, nameof(commissionRate));

        return new SellerOrder(
            SequentialGuid.New(now),
            orderId,
            sellerId,
            sellerOrderNumber.Trim(),
            decimal.Round(subtotal, 2),
            decimal.Round(discountAmount, 2),
            decimal.Round(shippingAmount, 2),
            decimal.Round(taxAmount, 2),
            commissionRate,
            now);
    }

    public void AddItem(OrderItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
    }

    public void ChangeStatus(SellerOrderStatus to, string? note, Guid? changedByUserId, DateTimeOffset now)
    {
        OrderStatusTransition.EnsureCanTransition(Status, to);
        Status = to;
        UpdatedAt = now;
        _history.Add(SellerOrderStatusHistory.Create(Id, SellerOrderNumber, Status, to, note, changedByUserId, now));

        if (to == SellerOrderStatus.Shipped)
        {
            ShippedAt ??= now;
        }
        else if (to == SellerOrderStatus.Delivered)
        {
            DeliveredAt ??= now;
        }
    }

    public void Cancel(string reason, Guid? cancelledByUserId, DateTimeOffset now)
    {
        if (!OrderStatusTransition.IsAllowed(Status, SellerOrderStatus.Cancelled))
        {
            throw new InvalidStateTransitionException("SellerOrder", Status.ToString(), SellerOrderStatus.Cancelled.ToString());
        }

        Status = SellerOrderStatus.Cancelled;
        CancelledAt = now;
        CancellationReason = reason?.Trim();
        UpdatedAt = now;
        _history.Add(SellerOrderStatusHistory.Create(Id, SellerOrderNumber, Status, SellerOrderStatus.Cancelled, reason, cancelledByUserId, now));
    }

    public void Ship(string carrierName, string trackingNumber, string? trackingUrl, DateTimeOffset? estimatedDelivery, DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(carrierName, nameof(carrierName));
        Guard.NotNullOrWhiteSpace(trackingNumber, nameof(trackingNumber));

        OrderStatusTransition.EnsureCanTransition(Status, SellerOrderStatus.Shipped);

        CarrierName = carrierName.Trim();
        TrackingNumber = trackingNumber.Trim();
        TrackingUrl = trackingUrl?.Trim();
        EstimatedDeliveryAt = estimatedDelivery;
        ShippedAt = now;
        Status = SellerOrderStatus.Shipped;
        UpdatedAt = now;
        _history.Add(SellerOrderStatusHistory.Create(Id, SellerOrderNumber, SellerOrderStatus.Packed, SellerOrderStatus.Shipped, $"Tracking {TrackingNumber}", null, now));
    }

    public void MarkDelivered(DateTimeOffset now)
    {
        OrderStatusTransition.EnsureCanTransition(Status, SellerOrderStatus.Delivered);
        Status = SellerOrderStatus.Delivered;
        DeliveredAt = now;
        UpdatedAt = now;
    }

    public void AddSellerNote(string? note, DateTimeOffset now)
    {
        SellerNote = note?.Trim();
        UpdatedAt = now;
    }

    public static string GenerateSellerOrderNumber(string orderNumber, int index) =>
        $"{orderNumber}-{index + 1:D2}";
}
