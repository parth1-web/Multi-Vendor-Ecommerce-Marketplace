using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Events;

/// <summary>Raised when a customer completes checkout. Triggers notifications, analytics and cache eviction.</summary>
public sealed record OrderCreatedEvent(Guid OrderId, string OrderNumber, Guid CustomerId, int SellerCount, decimal TotalAmount, DateTimeOffset OccurredOn)
    : DomainEvent(SequentialGuid.New(), OccurredOn)
{
    public override string EventName => nameof(OrderCreatedEvent);
}

public sealed record OrderStatusChangedEvent(Guid OrderId, string OrderNumber, OrderStatus PreviousStatus, OrderStatus NewStatus, DateTimeOffset OccurredOn)
    : DomainEvent(SequentialGuid.New(), OccurredOn)
{
    public override string EventName => nameof(OrderStatusChangedEvent);
}

public sealed record OrderCancelledEvent(Guid OrderId, string OrderNumber, Guid CustomerId, string Reason, DateTimeOffset OccurredOn)
    : DomainEvent(SequentialGuid.New(), OccurredOn)
{
    public override string EventName => nameof(OrderCancelledEvent);
}

public sealed record PaymentSucceededEvent(Guid PaymentId, Guid OrderId, Guid CustomerId, decimal Amount, string TransactionReference, DateTimeOffset OccurredOn)
    : DomainEvent(SequentialGuid.New(), OccurredOn)
{
    public override string EventName => nameof(PaymentSucceededEvent);
}

public sealed record PaymentFailedEvent(Guid PaymentId, Guid OrderId, string Reason, DateTimeOffset OccurredOn)
    : DomainEvent(SequentialGuid.New(), OccurredOn)
{
    public override string EventName => nameof(PaymentFailedEvent);
}

public sealed record RefundStatusChangedEvent(Guid RefundId, Guid OrderId, Guid CustomerId, RefundStatus Status, DateTimeOffset OccurredOn)
    : DomainEvent(SequentialGuid.New(), OccurredOn)
{
    public override string EventName => nameof(RefundStatusChangedEvent);
}

public sealed record SellerStatusChangedEvent(Guid SellerId, Guid SellerUserId, SellerStatus PreviousStatus, SellerStatus NewStatus, string? Reason, DateTimeOffset OccurredOn)
    : DomainEvent(SequentialGuid.New(), OccurredOn)
{
    public override string EventName => nameof(SellerStatusChangedEvent);
}

public sealed record ProductApprovalChangedEvent(Guid ProductId, Guid SellerId, ProductStatus Status, string? Reason, DateTimeOffset OccurredOn)
    : DomainEvent(SequentialGuid.New(), OccurredOn)
{
    public override string EventName => nameof(ProductApprovalChangedEvent);
}

public sealed record InventoryLowStockEvent(Guid InventoryId, Guid ProductId, Guid ProductVariantId, Guid SellerId, int SellableQuantity, int Threshold, DateTimeOffset OccurredOn)
    : DomainEvent(SequentialGuid.New(), OccurredOn)
{
    public override string EventName => nameof(InventoryLowStockEvent);
}

public sealed record ReviewCreatedEvent(Guid ReviewId, Guid ProductId, Guid SellerId, int Rating, DateTimeOffset OccurredOn)
    : DomainEvent(SequentialGuid.New(), OccurredOn)
{
    public override string EventName => nameof(ReviewCreatedEvent);
}

public sealed record CommissionCreatedEvent(Guid CommissionId, Guid SellerOrderId, Guid SellerId, decimal CommissionAmount, decimal SellerAmount, DateTimeOffset OccurredOn)
    : DomainEvent(SequentialGuid.New(), OccurredOn)
{
    public override string EventName => nameof(CommissionCreatedEvent);
}

public sealed record NotificationRequestedEvent(Guid UserId, NotificationType Type, string Title, string Body, string? Link, NotificationAudience Audience, DateTimeOffset OccurredOn)
    : DomainEvent(SequentialGuid.New(), OccurredOn)
{
    public override string EventName => nameof(NotificationRequestedEvent);
}

public sealed record UserRegisteredEvent(Guid UserId, string Email, UserRole Role, DateTimeOffset OccurredOn)
    : DomainEvent(SequentialGuid.New(), OccurredOn)
{
    public override string EventName => nameof(UserRegisteredEvent);
}
