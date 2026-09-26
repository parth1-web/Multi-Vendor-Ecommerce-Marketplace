using Marketplace.Domain.Common;

namespace Marketplace.Application.Common.Interfaces;

/// <summary>Pushes real-time messages to the SignalR hub. Implemented in the API layer.</summary>
public interface IRealtimeNotifier
{
    Task OrderCreatedAsync(Guid orderId, string orderNumber, Guid customerId, IReadOnlyCollection<Guid> sellerIds, decimal totalAmount, CancellationToken cancellationToken = default);

    Task OrderUpdatedAsync(Guid orderId, string orderNumber, object payload, CancellationToken cancellationToken = default);

    Task PaymentUpdatedAsync(Guid paymentId, Guid orderId, string status, CancellationToken cancellationToken = default);

    Task RefundUpdatedAsync(Guid refundId, Guid orderId, Guid customerId, string status, CancellationToken cancellationToken = default);

    Task NotificationCreatedAsync(Guid userId, object notification, CancellationToken cancellationToken = default);

    Task InventoryLowAsync(Guid sellerId, object payload, CancellationToken cancellationToken = default);

    Task SellerStatusChangedAsync(Guid sellerId, string status, CancellationToken cancellationToken = default);

    Task AdminMetricsUpdatedAsync(object payload, CancellationToken cancellationToken = default);
}

/// <summary>No-op notifier used in tests and by non-real-time hosts.</summary>
public sealed class NullRealtimeNotifier : IRealtimeNotifier
{
    public Task OrderCreatedAsync(Guid orderId, string orderNumber, Guid customerId, IReadOnlyCollection<Guid> sellerIds, decimal totalAmount, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task OrderUpdatedAsync(Guid orderId, string orderNumber, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task PaymentUpdatedAsync(Guid paymentId, Guid orderId, string status, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RefundUpdatedAsync(Guid refundId, Guid orderId, Guid customerId, string status, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotificationCreatedAsync(Guid userId, object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task InventoryLowAsync(Guid sellerId, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SellerStatusChangedAsync(Guid sellerId, string status, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task AdminMetricsUpdatedAsync(object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>SignalR group naming, kept in one place so client and server cannot drift.</summary>
public static class RealtimeGroups
{
    public const string Admin = "admin";

    public static string User(Guid userId) => $"user:{userId:N}";

    public static string Seller(Guid sellerId) => $"seller:{sellerId:N}";

    public static string Order(Guid orderId) => $"order:{orderId:N}";
}

/// <summary>Client method names pushed over the hub.</summary>
public static class RealtimeMethods
{
    public const string NotificationCreated = "NotificationCreated";
    public const string OrderCreated = "OrderCreated";
    public const string OrderUpdated = "OrderUpdated";
    public const string OrderStatusChanged = "OrderStatusChanged";
    public const string PaymentUpdated = "PaymentUpdated";
    public const string RefundUpdated = "RefundUpdated";
    public const string InventoryLow = "InventoryLow";
    public const string SellerStatusChanged = "SellerStatusChanged";
    public const string NewReview = "NewReview";
    public const string PlatformMetricsUpdated = "PlatformMetricsUpdated";
    public const string CartUpdated = "CartUpdated";
}
