using Marketplace.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Marketplace.API.Hubs;

/// <summary>
/// The single real-time hub. Group membership is derived from the authenticated token —
/// a client cannot ask to join someone else's group.
/// </summary>
public sealed class MarketplaceHub(
    ICurrentUser currentUser,
    IRepository<Domain.Orders.Order> orders) : Hub
{
    public const string Route = "/hubs/marketplace";

    public override async Task OnConnectedAsync()
    {
        if (currentUser.IsAuthenticated)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.User(currentUser.UserId)).ConfigureAwait(false);

            if (currentUser.SellerId is { } sellerId)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Seller(sellerId)).ConfigureAwait(false);
            }

            if (currentUser.IsAdmin)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Admin).ConfigureAwait(false);
            }
        }

        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    /// <summary>Subscribes the connection to updates for one order the caller owns.</summary>
    public async Task JoinOrder(Guid orderId)
    {
        if (!currentUser.IsAuthenticated)
        {
            throw new HubException("Authentication is required.");
        }

        var allowed = await IsParticipantAsync(orderId).ConfigureAwait(false);
        if (!allowed)
        {
            throw new HubException("You do not have access to this order.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Order(orderId)).ConfigureAwait(false);
    }

    public async Task LeaveOrder(Guid orderId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, RealtimeGroups.Order(orderId)).ConfigureAwait(false);

    /// <summary>Round-trip probe used by the client to measure latency.</summary>
    public DateTimeOffset Ping() => DateTimeOffset.UtcNow;

    private async Task<bool> IsParticipantAsync(Guid orderId)
    {
        if (currentUser.IsAdmin)
        {
            return true;
        }

        var order = await orders.Query().AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new { o.CustomerId, SellerIds = o.SellerOrders.Select(so => so.SellerId).ToList() })
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (order is null)
        {
            return false;
        }

        if (order.CustomerId == currentUser.UserId)
        {
            return true;
        }

        return currentUser.SellerId is { } sellerId && order.SellerIds.Contains(sellerId);
    }
}

/// <summary>
/// Publishes domain events to the hub. Lives in the API layer because SignalR is a
/// transport concern; the Application layer only sees <see cref="IRealtimeNotifier"/>.
/// </summary>
public sealed class SignalRRealtimeNotifier(IHubContext<MarketplaceHub> hub, ILogger<SignalRRealtimeNotifier> logger) : IRealtimeNotifier
{
    public Task OrderCreatedAsync(Guid orderId, string orderNumber, Guid customerId, IReadOnlyCollection<Guid> sellerIds, decimal totalAmount, CancellationToken cancellationToken = default)
    {
        var payload = new { OrderId = orderId, OrderNumber = orderNumber, TotalAmount = totalAmount, SellerCount = sellerIds.Count };

        var tasks = new List<Task>
        {
            hub.Clients.Group(RealtimeGroups.User(customerId)).SendAsync(RealtimeMethods.OrderCreated, payload, cancellationToken)
        };

        foreach (var sellerId in sellerIds)
        {
            tasks.Add(hub.Clients.Group(RealtimeGroups.Seller(sellerId)).SendAsync(RealtimeMethods.OrderCreated, payload, cancellationToken));
        }

        tasks.Add(hub.Clients.Group(RealtimeGroups.Admin).SendAsync(RealtimeMethods.OrderCreated, payload, cancellationToken));

        return Safe(Task.WhenAll(tasks), "OrderCreated");
    }

    public Task OrderUpdatedAsync(Guid orderId, string orderNumber, object payload, CancellationToken cancellationToken = default) =>
        Safe(
            Task.WhenAll(
                hub.Clients.Group(RealtimeGroups.Order(orderId)).SendAsync(RealtimeMethods.OrderUpdated, payload, cancellationToken),
                hub.Clients.All.SendAsync(RealtimeMethods.OrderUpdated, payload, cancellationToken)),
            "OrderUpdated");

    public Task PaymentUpdatedAsync(Guid paymentId, Guid orderId, string status, CancellationToken cancellationToken = default) =>
        Safe(hub.Clients.Group(RealtimeGroups.Order(orderId)).SendAsync(RealtimeMethods.PaymentUpdated, new { paymentId, orderId, status }, cancellationToken), "PaymentUpdated");

    public Task RefundUpdatedAsync(Guid refundId, Guid orderId, Guid customerId, string status, CancellationToken cancellationToken = default) =>
        Safe(
            Task.WhenAll(
                hub.Clients.Group(RealtimeGroups.User(customerId)).SendAsync(RealtimeMethods.RefundUpdated, new { refundId, orderId, status }, cancellationToken),
                hub.Clients.Group(RealtimeGroups.Admin).SendAsync(RealtimeMethods.RefundUpdated, new { refundId, orderId, status }, cancellationToken)),
            "RefundUpdated");

    public Task NotificationCreatedAsync(Guid userId, object notification, CancellationToken cancellationToken = default) =>
        Safe(hub.Clients.Group(RealtimeGroups.User(userId)).SendAsync(RealtimeMethods.NotificationCreated, notification, cancellationToken), "NotificationCreated");

    public Task InventoryLowAsync(Guid sellerId, object payload, CancellationToken cancellationToken = default) =>
        Safe(hub.Clients.Group(RealtimeGroups.Seller(sellerId)).SendAsync(RealtimeMethods.InventoryLow, payload, cancellationToken), "InventoryLow");

    public Task SellerStatusChangedAsync(Guid sellerId, string status, CancellationToken cancellationToken = default) =>
        Safe(hub.Clients.Group(RealtimeGroups.Seller(sellerId)).SendAsync(RealtimeMethods.SellerStatusChanged, new { sellerId, status }, cancellationToken), "SellerStatusChanged");

    public Task AdminMetricsUpdatedAsync(object payload, CancellationToken cancellationToken = default) =>
        Safe(hub.Clients.Group(RealtimeGroups.Admin).SendAsync(RealtimeMethods.PlatformMetricsUpdated, payload, cancellationToken), "PlatformMetricsUpdated");

    private async Task Safe(Task task, string name)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Real-time delivery is best effort; the database is the source of truth.
            logger.LogWarning(ex, "Failed to broadcast {Event}", name);
        }
    }
}
