using Marketplace.Application.Common.Extensions;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Inventory.Abstractions;
using Marketplace.Application.Modules.Orders.Abstractions;
using Marketplace.Application.Modules.Orders.DTOs;
using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Events;
using Marketplace.Domain.Inventory;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Sellers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderEntity = Marketplace.Domain.Orders.Order;

namespace Marketplace.Application.Modules.Orders.Services;

/// <summary>
/// Order reads and status transitions for customers, sellers and admins. Ownership is
/// checked in the query for every read, and seller status changes only ever touch the
/// caller's own sub-order.
/// </summary>
public sealed class OrderService(
    IRepository<OrderEntity> orders,
    IRepository<Domain.Orders.SellerOrder> sellerOrders,
    IRepository<Payment> payments,
    IRepository<InventoryReservation> reservations,
    IRepository<SellerStore> stores,
    IInventoryService inventory,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    IClock clock,
    IAuditService auditService,
    INotificationService notificationService,
    IRealtimeNotifier realtime,
    ILogger<OrderService> logger) : IOrderService
{
    private static readonly OrderStatus[] Timeline =
    [
        OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.Processing,
        OrderStatus.Packed, OrderStatus.Shipped, OrderStatus.Delivered
    ];

    public async Task<PagedResult<OrderListItemResponse>> ListOwnAsync(OrderListQuery query, CancellationToken cancellationToken = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var source = orders.Query().AsNoTracking().Where(o => o.CustomerId == currentUser.UserId);

        source = ApplyOrderFilters(source, query);

        return await source.ToPagedResultAsync(page, o => new OrderListItemResponse(
            o.Id, o.OrderNumber, o.Status, o.PlacedAt, o.TotalAmount, o.Currency, o.IsPaid,
            o.Items.FirstOrDefault()?.ProductName, o.Items.FirstOrDefault()?.ProductImage,
            o.Items.Count, o.SellerOrders.Count, string.Join(", ", o.SellerOrders.Select(so => so.SellerId.ToString()).Take(3))),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<OrderResponse>> GetOwnAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await orders.Query()
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.SellerOrders)
            // Two levels of collection navigation need ThenInclude; a projected Select inside
            // Include is not a property access and EF rejects it when the query is compiled.
            .ThenInclude(so => so.Items)
            .Include(o => o.History)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == currentUser.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            // Someone else's order is reported exactly like a missing one, so the endpoint
            // never confirms that an order id exists.
            return Result<OrderResponse>.Failure("Order not found.", ResultErrorCodes.NotFound);
        }

        return Result<OrderResponse>.Success(await BuildAsync(order, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result> CancelOwnAsync(Guid orderId, CancelOrderRequest request, CancellationToken cancellationToken = default)
    {
        var order = await orders.Query()
            .Include(o => o.SellerOrders)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == currentUser.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return Result.Failure("Order not found.", ResultErrorCodes.NotFound);
        }

        if (!OrderStatusTransition.IsCancellableByCustomer(order.Status))
        {
            return Result.Failure("This order can no longer be cancelled. Contact support for help.");
        }

        var now = clock.UtcNow;
        order.Cancel(request.Reason ?? "Cancelled by the customer.", currentUser.UserId, now);
        order.AddDomainEvent(new OrderCancelledEvent(order.Id, order.OrderNumber, order.CustomerId, request.Reason ?? "customer", now));

        // Release every reservation this order holds.
        var held = await reservations.Query()
            .Where(r => r.OrderId == order.Id && r.ReleasedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var reservation in held)
        {
            await inventory.ReleaseReservationAsync(reservation, "order-cancelled", cancellationToken).ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.OrderCancelled, nameof(OrderEntity), order.Id, order.OrderNumber,
            new { Reason = request.Reason }, cancellationToken).ConfigureAwait(false);

        await notificationService.NotifyAsync(order.CustomerId, NotificationType.OrderCancelled,
            $"Order {order.OrderNumber} cancelled",
            "Your order has been cancelled and any reserved stock has been released.",
            $"/orders/{order.OrderNumber}", NotificationAudience.Customer, cancellationToken).ConfigureAwait(false);

        await realtime.OrderUpdatedAsync(order.Id, order.OrderNumber, new { order.Status }, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Order {OrderNumber} cancelled by the customer", order.OrderNumber);

        return Result.Success();
    }

    public async Task<PagedResult<SellerOrderSummaryResponse>> ListSellerOrdersAsync(OrderListQuery query, CancellationToken cancellationToken = default)
    {
        if (currentUser.SellerId is not { } sellerId)
        {
            return PagedResult<SellerOrderSummaryResponse>.Empty(query.Page ?? 1, query.PageSize ?? 20);
        }

        var page = new PageRequest(query.Page, query.PageSize);
        // The parent order is projected into the response, so it has to be loaded with it.
        var source = sellerOrders.Query().AsNoTracking().Include(so => so.Order).Where(so => so.SellerId == sellerId);

        if (query.Status is { } status)
        {
            source = source.Where(so => (int)so.Status == (int)status);
        }

        if (query.From is { } from)
        {
            source = source.Where(so => so.CreatedAt >= from);
        }

        if (query.To is { } to)
        {
            source = source.Where(so => so.CreatedAt <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            source = source.Where(so => EF.Functions.Like(so.SellerOrderNumber, term));
        }

        source = query.Sort switch
        {
            "oldest" => source.OrderBy(so => so.CreatedAt),
            "highest" => source.OrderByDescending(so => so.TotalAmount),
            "lowest" => source.OrderBy(so => so.TotalAmount),
            _ => source.OrderByDescending(so => so.CreatedAt)
        };

        var result = await source.ToPagedResultAsync(page, so => new SellerOrderSummaryResponse(
            so.Id, so.SellerOrderNumber, so.SellerId, so.OrderId, so.Order!.OrderNumber, string.Empty, so.Status,
            so.Subtotal, so.DiscountAmount, so.ShippingAmount, so.TotalAmount,
            so.CommissionRate, so.CommissionAmount, so.SellerEarnings,
            so.CarrierName, so.TrackingNumber, so.EstimatedDeliveryAt, so.Items.Count), cancellationToken)
            .ConfigureAwait(false);

        var named = await WithStoreNamesAsync(result.Items, cancellationToken).ConfigureAwait(false);
        return new PagedResult<SellerOrderSummaryResponse>(named, result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<Result<SellerOrderSummaryResponse>> GetSellerOrderAsync(Guid sellerOrderId, CancellationToken cancellationToken = default)
    {
        if (currentUser.SellerId is not { } sellerId)
        {
            return Result<SellerOrderSummaryResponse>.Failure("Seller access is required.");
        }

        var sellerOrder = await sellerOrders.Query()
            .AsNoTracking()
            .Include(so => so.Items)
            .Include(so => so.History)
            .Include(so => so.Order)
            .FirstOrDefaultAsync(so => so.Id == sellerOrderId && so.SellerId == sellerId, cancellationToken)
            .ConfigureAwait(false);

        if (sellerOrder is null)
        {
            return Result<SellerOrderSummaryResponse>.Failure("Order not found.", ResultErrorCodes.NotFound);
        }

        var dto = new SellerOrderSummaryResponse(
            sellerOrder.Id, sellerOrder.SellerOrderNumber, sellerOrder.SellerId,
            sellerOrder.OrderId, sellerOrder.Order!.OrderNumber, string.Empty, sellerOrder.Status,
            sellerOrder.Subtotal, sellerOrder.DiscountAmount, sellerOrder.ShippingAmount, sellerOrder.TotalAmount,
            sellerOrder.CommissionRate, sellerOrder.CommissionAmount, sellerOrder.SellerEarnings,
            sellerOrder.CarrierName, sellerOrder.TrackingNumber, sellerOrder.EstimatedDeliveryAt, sellerOrder.Items.Count);

        var named = await WithStoreNamesAsync([dto], cancellationToken).ConfigureAwait(false);
        return Result<SellerOrderSummaryResponse>.Success(named[0]);
    }

    public async Task<Result> UpdateSellerOrderStatusAsync(Guid sellerOrderId, UpdateOrderStatusRequest request, CancellationToken cancellationToken = default)
    {
        if (currentUser.SellerId is not { } sellerId)
        {
            return Result.Failure("Seller access is required.");
        }

        var sellerOrder = await sellerOrders.Query()
            .Include(so => so.Items)
            .FirstOrDefaultAsync(so => so.Id == sellerOrderId && so.SellerId == sellerId, cancellationToken)
            .ConfigureAwait(false);

        if (sellerOrder is null)
        {
            return Result.Failure("Order not found.", ResultErrorCodes.NotFound);
        }

        var now = clock.UtcNow;
        var previous = sellerOrder.Status;

        if (request.Status == SellerOrderStatus.Shipped && !string.IsNullOrWhiteSpace(request.TrackingNumber))
        {
            sellerOrder.Ship(request.CarrierName ?? "Courier", request.TrackingNumber, request.TrackingUrl, request.EstimatedDeliveryAt, now);
        }
        else
        {
            sellerOrder.ChangeStatus(request.Status, request.Note, currentUser.UserId, now);
        }

        var order = await orders.Query()
            .Include(o => o.SellerOrders)
            .FirstOrDefaultAsync(o => o.Id == sellerOrder.OrderId, cancellationToken)
            .ConfigureAwait(false);

        order?.SyncFromSellerOrder(sellerOrder.Status, now);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.OrderStatusChanged, nameof(SellerOrder), sellerOrder.Id, sellerOrder.SellerOrderNumber,
            new { From = previous.ToString(), To = sellerOrder.Status.ToString(), request.Note }, cancellationToken).ConfigureAwait(false);

        if (order is not null)
        {
            await NotifyCustomerAsync(order, sellerOrder.Status, now, cancellationToken).ConfigureAwait(false);
            await realtime.OrderUpdatedAsync(order.Id, order.OrderNumber, new
            {
                order.Status,
                SellerOrderId = sellerOrder.Id,
                SellerStatus = sellerOrder.Status,
                TrackingNumber = sellerOrder.TrackingNumber
            }, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }

    public async Task<PagedResult<OrderListItemResponse>> ListAllAsync(OrderListQuery query, CancellationToken cancellationToken = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var source = ApplyOrderFilters(orders.Query().AsNoTracking(), query);

        var result = await source.ToPagedResultAsync(page, o => new OrderListItemResponse(
            o.Id, o.OrderNumber, o.Status, o.PlacedAt, o.TotalAmount, o.Currency, o.IsPaid,
            o.Items.FirstOrDefault()?.ProductName, o.Items.FirstOrDefault()?.ProductImage,
            o.Items.Count, o.SellerOrders.Count, string.Empty), cancellationToken).ConfigureAwait(false);

        // Which stores an order went to is the first thing anybody supporting it wants to know,
        // and it is in the response, so it is filled in rather than left as an empty string.
        return await WithStoreNamesAsync(result, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<OrderResponse>> GetByIdForAdminAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await orders.Query()
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.SellerOrders)
            .Include(o => o.History)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            // Someone else's order is reported exactly like a missing one, so the endpoint
            // never confirms that an order id exists.
            return Result<OrderResponse>.Failure("Order not found.", ResultErrorCodes.NotFound);
        }

        return Result<OrderResponse>.Success(await BuildAsync(order, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result> UpdateStatusAsAdminAsync(Guid orderId, OrderStatus status, string? note, CancellationToken cancellationToken = default)
    {
        var order = await orders.Query()
            .Include(o => o.SellerOrders)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return Result.Failure("Order not found.", ResultErrorCodes.NotFound);
        }

        var now = clock.UtcNow;
        var previous = order.Status;
        order.ChangeStatus(status, note ?? "Updated by an administrator", currentUser.UserId, now);

        foreach (var sellerOrder in order.SellerOrders)
        {
            if (OrderStatusTransition.IsAllowed(sellerOrder.Status, (SellerOrderStatus)status))
            {
                sellerOrder.ChangeStatus((SellerOrderStatus)status, note, currentUser.UserId, now);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.OrderStatusChanged, nameof(OrderEntity), order.Id, order.OrderNumber,
            new { From = previous.ToString(), To = status.ToString(), Note = note }, cancellationToken).ConfigureAwait(false);

        await NotifyCustomerAsync(order, (SellerOrderStatus)status, now, cancellationToken).ConfigureAwait(false);
        await realtime.OrderUpdatedAsync(order.Id, order.OrderNumber, new { order.Status }, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    private static IQueryable<OrderEntity> ApplyOrderFilters(IQueryable<OrderEntity> source, OrderListQuery query)
    {
        if (query.Status is { } status)
        {
            source = source.Where(o => o.Status == status);
        }

        if (query.From is { } from)
        {
            source = source.Where(o => o.PlacedAt >= from);
        }

        if (query.To is { } to)
        {
            source = source.Where(o => o.PlacedAt <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            source = source.Where(o =>
                EF.Functions.Like(o.OrderNumber, term) ||
                o.Items.Any(i => EF.Functions.Like(i.ProductName, term)));
        }

        return query.Sort switch
        {
            "oldest" => source.OrderBy(o => o.PlacedAt),
            "highest" => source.OrderByDescending(o => o.TotalAmount),
            "lowest" => source.OrderBy(o => o.TotalAmount),
            _ => source.OrderByDescending(o => o.PlacedAt)
        };
    }

    private async Task<OrderResponse> BuildAsync(OrderEntity order, CancellationToken cancellationToken)
    {
        var payment = await payments.Query().AsNoTracking()
            .Where(p => p.OrderId == order.Id)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var sellerIds = order.SellerOrders.Select(so => so.SellerId).Distinct().ToList();
        var storeList = await stores.Query().AsNoTracking()
            .Where(s => sellerIds.Contains(s.SellerId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var storeNames = storeList.ToDictionary(s => s.SellerId, s => s.Name);
        var now = clock.UtcNow;

        var items = order.Items.Select(i => new OrderItemResponse(
            i.Id, i.ProductId, i.ProductVariantId, i.SellerId,
            storeNames.GetValueOrDefault(i.SellerId, "Seller"),
            i.ProductName, i.ProductImage, i.VariantName, i.Sku,
            i.Quantity, i.UnitPrice, i.LineTotal,
            i.IsReviewed, !i.IsReviewed && order.Status is OrderStatus.Delivered or OrderStatus.Completed,
            !i.IsRefundRequested && order.Status == OrderStatus.Delivered)).ToList();

        var sellerOrderSummaries = order.SellerOrders.Select(so => new SellerOrderSummaryResponse(
            so.Id, so.SellerOrderNumber, so.SellerId, so.OrderId, order.OrderNumber,
            storeNames.GetValueOrDefault(so.SellerId, "Seller"), so.Status,
            so.Subtotal, so.DiscountAmount, so.ShippingAmount, so.TotalAmount,
            so.CommissionRate, so.CommissionAmount, so.SellerEarnings,
            so.CarrierName, so.TrackingNumber, so.EstimatedDeliveryAt, so.Items.Count)).ToList();

        var history = order.History.OrderBy(h => h.CreatedAt).ToList();

        var timeline = Timeline.Select((step, index) =>
        {
            var entry = history.FirstOrDefault(h => h.ToStatus == step);
            var reachedIndex = Array.IndexOf(Timeline, step);
            var currentIndex = Array.IndexOf(Timeline, order.Status);

            return new OrderTimelineStepResponse(
                step.ToString(),
                Label(step),
                entry?.CreatedAt ?? (order.PlacedAt == default ? null : reachedIndex == 0 ? order.PlacedAt : null),
                reachedIndex <= currentIndex,
                reachedIndex == currentIndex,
                entry?.Note);
        }).ToList();

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Completed or OrderStatus.Returned)
        {
            var terminal = history.LastOrDefault();
            timeline.Add(new OrderTimelineStepResponse(order.Status.ToString(), Label(order.Status), terminal?.CreatedAt, true, true, terminal?.Note));
        }

        return new OrderResponse(
            order.Id, order.OrderNumber, order.Status, order.PlacedAt,
            order.Subtotal, order.DiscountAmount, order.ShippingAmount, order.TaxAmount,
            order.TotalAmount, order.RefundedAmount, order.Currency, order.IsPaid,
            payment?.Provider.ToString() ?? "Mock", payment?.Status.ToString() ?? "Initiated",
            order.CouponCode,
            new AddressSnapshotResponse(
                order.ShippingAddressSnapshot.Label,
                order.ShippingAddressSnapshot.RecipientName,
                order.ShippingAddressSnapshot.PhoneNumber,
                order.ShippingAddressSnapshot.Line1,
                order.ShippingAddressSnapshot.Line2,
                order.ShippingAddressSnapshot.City,
                order.ShippingAddressSnapshot.State,
                order.ShippingAddressSnapshot.PostalCode,
                order.ShippingAddressSnapshot.Country),
            order.CustomerNote,
            order.CancellationReason,
            order.SellerOrders.Count,
            items,
            sellerOrderSummaries,
            timeline);
    }

    private async Task NotifyCustomerAsync(OrderEntity order, SellerOrderStatus status, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (type, title) = status switch
        {
            SellerOrderStatus.Confirmed => (NotificationType.OrderConfirmed, "Your order is confirmed"),
            SellerOrderStatus.Shipped => (NotificationType.OrderShipped, "Your order is on its way"),
            SellerOrderStatus.Delivered => (NotificationType.OrderDelivered, "Your order has been delivered"),
            SellerOrderStatus.Cancelled => (NotificationType.OrderCancelled, "Part of your order was cancelled"),
            _ => (NotificationType.SystemNotification, "Your order was updated")
        };

        await notificationService.NotifyAsync(order.CustomerId, type, title,
            $"Order {order.OrderNumber} is now {status}.", $"/orders/{order.OrderNumber}",
            NotificationAudience.Customer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fills in the store names on a page of orders, one query for the whole page.
    /// </summary>
    /// <remarks>
    /// Returns a new page rather than editing the one it was given. These are records, so a copy of
    /// the list is a copy of the elements, and writing a corrected record into the copy leaves the
    /// page the caller is holding exactly as it was.
    /// </remarks>
    private async Task<PagedResult<OrderListItemResponse>> WithStoreNamesAsync(
        PagedResult<OrderListItemResponse> page, CancellationToken cancellationToken)
    {
        if (page.Items.Count == 0)
        {
            return page;
        }

        var orderIds = page.Items.Select(i => i.Id).ToList();
        var sellersByOrder = await sellerOrders.Query().AsNoTracking()
            .Where(so => orderIds.Contains(so.OrderId))
            .Select(so => new { so.OrderId, so.SellerId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var sellerIds = sellersByOrder.Select(s => s.SellerId).Distinct().ToList();
        var storeList = await stores.Query().AsNoTracking()
            .Where(s => sellerIds.Contains(s.SellerId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var names = storeList.ToDictionary(s => s.SellerId, s => s.Name);
        var byOrder = sellersByOrder
            .GroupBy(s => s.OrderId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(s => names.GetValueOrDefault(s.SellerId, "Seller")).Distinct()));

        var items = page.Items
            .Select(item => item with { SellerNames = byOrder.GetValueOrDefault(item.Id) })
            .ToList();

        return new PagedResult<OrderListItemResponse>(items, page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>
    /// Returns a copy of these seller orders with the store name filled in.
    /// </summary>
    /// <remarks>
    /// A new list rather than an edit, for the same reason as the order list: these are records,
    /// so writing into a copy of the list leaves the caller's page untouched. One query for the
    /// whole page, not one per row.
    /// </remarks>
    private async Task<List<SellerOrderSummaryResponse>> WithStoreNamesAsync(
        IEnumerable<SellerOrderSummaryResponse> items, CancellationToken cancellationToken)
    {
        var list = items as List<SellerOrderSummaryResponse> ?? items.ToList();

        if (list.Count == 0)
        {
            return list;
        }

        var sellerIds = list.Select(i => i.SellerId).Distinct().ToList();
        var storeList = await stores.Query().AsNoTracking()
            .Where(s => sellerIds.Contains(s.SellerId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var names = storeList.ToDictionary(s => s.SellerId, s => s.Name);

        return list.Select(i => i with { StoreName = names.GetValueOrDefault(i.SellerId, "Seller") }).ToList();
    }



    private static string Label(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "Order placed",
        OrderStatus.Confirmed => "Confirmed",
        OrderStatus.Processing => "Processing",
        OrderStatus.Packed => "Packed",
        OrderStatus.Shipped => "Shipped",
        OrderStatus.Delivered => "Delivered",
        OrderStatus.Cancelled => "Cancelled",
        OrderStatus.Returned => "Returned",
        OrderStatus.Completed => "Completed",
        _ => status.ToString()
    };
}
