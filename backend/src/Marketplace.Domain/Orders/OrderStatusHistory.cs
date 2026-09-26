using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Orders;

/// <summary>Immutable audit trail of every order state change, used to render the tracking timeline.</summary>
public class OrderStatusHistory : Entity
{
    private OrderStatusHistory()
    {
        OrderNumber = string.Empty;
    }

    private OrderStatusHistory(Guid id, Guid orderId, string orderNumber, OrderStatus fromStatus, OrderStatus toStatus, string? note, Guid? changedByUserId, DateTimeOffset now)
        : base(id)
    {
        OrderId = orderId;
        OrderNumber = orderNumber;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Note = note;
        ChangedByUserId = changedByUserId;
        CreatedAt = now;
    }

    public Guid OrderId { get; private set; }

    public string OrderNumber { get; private set; }

    public OrderStatus FromStatus { get; private set; }

    public OrderStatus ToStatus { get; private set; }

    public string? Note { get; private set; }

    public Guid? ChangedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static OrderStatusHistory Create(Guid orderId, string orderNumber, OrderStatus fromStatus, OrderStatus toStatus, string? note, Guid? changedByUserId, DateTimeOffset now) =>
        new(SequentialGuid.New(now), orderId, orderNumber, fromStatus, toStatus, note, changedByUserId, now);
}

public class SellerOrderStatusHistory : Entity
{
    private SellerOrderStatusHistory()
    {
        SellerOrderNumber = string.Empty;
    }

    private SellerOrderStatusHistory(Guid id, Guid sellerOrderId, string sellerOrderNumber, SellerOrderStatus fromStatus, SellerOrderStatus toStatus, string? note, Guid? changedByUserId, DateTimeOffset now)
        : base(id)
    {
        SellerOrderId = sellerOrderId;
        SellerOrderNumber = sellerOrderNumber;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Note = note;
        ChangedByUserId = changedByUserId;
        CreatedAt = now;
    }

    public Guid SellerOrderId { get; private set; }

    public string SellerOrderNumber { get; private set; }

    public SellerOrderStatus FromStatus { get; private set; }

    public SellerOrderStatus ToStatus { get; private set; }

    public string? Note { get; private set; }

    public Guid? ChangedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static SellerOrderStatusHistory Create(Guid sellerOrderId, string sellerOrderNumber, SellerOrderStatus fromStatus, SellerOrderStatus toStatus, string? note, Guid? changedByUserId, DateTimeOffset now) =>
        new(SequentialGuid.New(now), sellerOrderId, sellerOrderNumber, fromStatus, toStatus, note, changedByUserId, now);
}
