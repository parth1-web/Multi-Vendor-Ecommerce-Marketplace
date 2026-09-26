using Marketplace.Application.Common.Extensions;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Auth.Abstractions;
using Marketplace.Application.Modules.Inventory.Abstractions;
using Marketplace.Application.Modules.Payments.Abstractions;
using Marketplace.Application.Modules.Payments.DTOs;
using Marketplace.Domain.Common;
using Marketplace.Domain.Commissions;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Events;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Refunds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderEntity = Marketplace.Domain.Orders.Order;

namespace Marketplace.Application.Modules.Payments.Services;

/// <summary>
/// Refund workflow: customer request → admin review → gateway refund → stock returned and
/// commission reversed. One request per order item is enforced by a unique index.
/// </summary>
public sealed class RefundService(
    IRepository<Refund> refunds,
    IRepository<OrderEntity> orders,
    IRepository<Payment> payments,
    IRepository<Commission> commissions,
    IInventoryService inventory,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    IClock clock,
    IAuditService auditService,
    INotificationService notificationService,
    IRealtimeNotifier realtime,
    IPaymentGatewayResolver gateways,
    ILogger<RefundService> logger) : IRefundService
{
    public async Task<Result<RefundResponse>> RequestAsync(CreateRefundRequest request, CancellationToken cancellationToken = default)
    {
        if (request.OrderItemIds.Count == 0)
        {
            return Result<RefundResponse>.Failure("Select at least one item to return.");
        }

        var order = await orders.Query()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId && o.CustomerId == currentUser.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            // Reported exactly like a missing order, so the endpoint never confirms that an
            // order id exists for somebody else.
            return Result<RefundResponse>.Failure("Order not found.", ResultErrorCodes.NotFound);
        }

        if (order.Status is not (OrderStatus.Delivered or OrderStatus.Completed))
        {
            return Result<RefundResponse>.Failure("Only a delivered order can be returned.");
        }

        var items = order.Items.Where(i => request.OrderItemIds.Contains(i.Id)).ToList();
        if (items.Count != request.OrderItemIds.Count)
        {
            return Result<RefundResponse>.Failure("One or more selected items do not belong to this order.");
        }

        if (items.Any(i => i.IsRefundRequested))
        {
            return Result<RefundResponse>.Failure("A refund has already been requested for one of these items.");
        }

        var alreadyRequested = await refunds.Query()
            .SelectMany(r => r.Items)
            .Where(ri => request.OrderItemIds.Contains(ri.OrderItemId))
            .Select(ri => ri.OrderItemId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (alreadyRequested.Count > 0)
        {
            return Result<RefundResponse>.Failure("A refund has already been requested for one of these items.");
        }

        var payment = await payments.Query().AsNoTracking()
            .Where(p => p.OrderId == order.Id)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (payment is null)
        {
            return Result<RefundResponse>.Failure("No payment was found for this order.");
        }

        var amount = decimal.Round(items.Sum(i => i.LineTotal - i.DiscountAmount), 2);
        if (amount <= 0m)
        {
            return Result<RefundResponse>.Failure("The refundable amount must be greater than zero.");
        }

        if (amount > payment.RefundableAmount)
        {
            return Result<RefundResponse>.Failure("The requested amount exceeds the refundable balance.");
        }

        var now = clock.UtcNow;
        var sellerId = items[0].SellerId;

        var refund = Refund.Create(order.Id, payment.Id, order.CustomerId, sellerId, amount, request.Reason, request.Description, now);
        foreach (var item in items)
        {
            refund.AddItem(item.Id, item.ProductId, item.ProductName, item.Sku, item.Quantity, item.LineTotal - item.DiscountAmount, now);
            item.MarkRefundRequested();
        }

        await refunds.AddAsync(refund, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditService.RecordAsync(AuditAction.RefundRequested, nameof(Refund), refund.Id, order.OrderNumber,
            new { Amount = amount, Reason = request.Reason, Items = items.Count }, cancellationToken).ConfigureAwait(false);

        await notificationService.NotifyAsync(order.CustomerId, NotificationType.RefundRequested,
            "Refund request received",
            $"We are reviewing your refund request for order {order.OrderNumber}.",
            $"/orders/{order.OrderNumber}", NotificationAudience.Customer, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Refund {RefundId} requested for order {OrderNumber}", refund.Id, order.OrderNumber);
        return Result<RefundResponse>.Success(await MapAsync(refund, cancellationToken).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<RefundResponse>> ListOwnAsync(CancellationToken cancellationToken = default)
    {
        var rows = await refunds.Query()
            .Include(r => r.Items)
            .AsNoTracking()
            .Where(r => r.CustomerId == currentUser.UserId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = new List<RefundResponse>(rows.Count);
        foreach (var row in rows)
        {
            result.Add(await MapAsync(row, cancellationToken).ConfigureAwait(false));
        }

        return result;
    }

    public async Task<Result<RefundResponse>> GetAsync(Guid refundId, CancellationToken cancellationToken = default)
    {
        var refund = await refunds.Query()
            .Include(r => r.Items)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == refundId && (currentUser.IsAdmin || r.CustomerId == currentUser.UserId), cancellationToken)
            .ConfigureAwait(false);

        if (refund is null)
        {
            return Result<RefundResponse>.Failure("Refund not found.", ResultErrorCodes.NotFound);
        }

        return Result<RefundResponse>.Success(await MapAsync(refund, cancellationToken).ConfigureAwait(false));
    }

    public async Task<PagedResult<RefundResponse>> ListAllAsync(RefundListQuery query, CancellationToken cancellationToken = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var source = refunds.Query().Include(r => r.Items).AsNoTracking();

        if (query.Status is { } status)
        {
            source = source.Where(r => r.Status == status);
        }

        if (query.OrderId is { } orderId)
        {
            source = source.Where(r => r.OrderId == orderId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            source = source.Where(r => EF.Functions.Like(r.Reason, term));
        }

        var rows = await source
            .OrderByDescending(r => r.CreatedAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = new List<RefundResponse>(rows.Count);
        foreach (var row in rows)
        {
            items.Add(await MapAsync(row, cancellationToken).ConfigureAwait(false));
        }

        return new PagedResult<RefundResponse>(items, page.Page, page.PageSize, await source.CountAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<RefundResponse>> ReviewAsync(Guid refundId, ReviewRefundRequest request, CancellationToken cancellationToken = default)
    {
        var refund = await refunds.Query()
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == refundId, cancellationToken)
            .ConfigureAwait(false);

        if (refund is null)
        {
            return Result<RefundResponse>.Failure("Refund not found.", ResultErrorCodes.NotFound);
        }

        var now = clock.UtcNow;
        var previous = refund.Status;

        switch (request.Action)
        {
            case ReviewRefundAction.Approve:
                refund.Approve(currentUser.UserId, request.Note, now);
                refund.MarkProcessing(now);
                break;
            case ReviewRefundAction.Reject:
                if (string.IsNullOrWhiteSpace(request.Note))
                {
                    return Result<RefundResponse>.Failure("A reason is required when rejecting a refund.");
                }

                refund.Reject(currentUser.UserId, request.Note, now);
                break;
            case ReviewRefundAction.MarkProcessing:
                refund.MarkProcessing(now);
                break;
        }

        var action = request.Action switch
        {
            ReviewRefundAction.Approve => AuditAction.RefundApproved,
            ReviewRefundAction.Reject => AuditAction.RefundRejected,
            _ => AuditAction.RefundRequested
        };

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(action, nameof(Refund), refund.Id, refund.Reason,
            new { From = previous.ToString(), To = refund.Status.ToString(), Note = request.Note }, cancellationToken).ConfigureAwait(false);

        if (request.Action == ReviewRefundAction.Approve)
        {
            var completion = await CompleteGatewayRefundAsync(refund, now, cancellationToken).ConfigureAwait(false);
            if (completion.IsFailure)
            {
                return completion;
            }
        }

        await NotifyAsync(refund, request.Action, request.Note, now, cancellationToken).ConfigureAwait(false);
        await realtime.RefundUpdatedAsync(refund.Id, refund.OrderId, refund.CustomerId, refund.Status.ToString(), cancellationToken).ConfigureAwait(false);

        return Result<RefundResponse>.Success(await MapAsync(refund, cancellationToken).ConfigureAwait(false));
    }

    private async Task<Result<RefundResponse>> CompleteGatewayRefundAsync(Refund refund, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var payment = await payments.Query().FirstOrDefaultAsync(p => p.Id == refund.PaymentId, cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            refund.MarkFailed("Payment record not found", now);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result<RefundResponse>.Failure("Payment record not found.", ResultErrorCodes.NotFound);
        }

        var gateway = gateways.Resolve(payment.Provider);
        var result = await gateway.RefundAsync(
            new RefundRequest(payment.Id, refund.Id, payment.GatewayPaymentId ?? payment.TransactionReference, refund.Amount, payment.Currency, refund.Reason),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            refund.MarkFailed(result.FailureReason ?? "The gateway refused the refund.", now);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogWarning("Gateway refund failed for {RefundId}: {Reason}", refund.Id, result.FailureReason);
            return Result<RefundResponse>.Failure(result.FailureReason ?? "The gateway refused the refund.");
        }

        refund.MarkCompleted(result.GatewayRefundId ?? "refund", now);
        payment.RecordRefund(refund.Amount, now);

        var order = await orders.Query()
            .Include(o => o.Items)
            .Include(o => o.SellerOrders)
            .FirstOrDefaultAsync(o => o.Id == refund.OrderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is not null)
        {
            order.RecordRefund(refund.Amount, now);

            // Put the returned goods back on the shelf.
            foreach (var refundItem in refund.Items)
            {
                var orderItem = order.Items.FirstOrDefault(i => i.Id == refundItem.OrderItemId);
                if (orderItem is null)
                {
                    continue;
                }

                await inventory.AdjustAsync(orderItem.ProductVariantId,
                    new Modules.Notifications.DTOs.AdjustStockRequest(refundItem.Quantity, "customer-return"),
                    cancellationToken).ConfigureAwait(false);
            }

            // Reverse the commission on every sub-order that contains a refunded line.
            foreach (var refundItem in refund.Items)
            {
                var sellerOrderId = order.Items.FirstOrDefault(i => i.Id == refundItem.OrderItemId)?.SellerOrderId;
                if (sellerOrderId is null)
                {
                    continue;
                }

                var commission = await commissions.Query()
                    .FirstOrDefaultAsync(c => c.SellerOrderId == sellerOrderId, cancellationToken)
                    .ConfigureAwait(false);

                commission?.Reverse($"refund-{refund.Id}", now);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<RefundResponse>.Success(new RefundResponse(Guid.Empty, refund.OrderId, string.Empty, refund.PaymentId, refund.Status, refund.Amount, refund.Reason, null, null, null, refund.RequestedAt, null, refund.CompletedAt, []));
    }

    private async Task NotifyAsync(Refund refund, ReviewRefundAction action, string? note, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (type, title, body) = action switch
        {
            ReviewRefundAction.Approve => (NotificationType.RefundApproved, "Refund approved", $"Your refund of {refund.Amount:0.00} has been approved and is on its way back to you."),
            ReviewRefundAction.Reject => (NotificationType.RefundRejected, "Refund declined", $"Your refund request was declined. {note}".Trim()),
            _ => (NotificationType.RefundRequested, "Refund update", $"Your refund is now {refund.Status}.")
        };

        await notificationService.NotifyAsync(refund.CustomerId, type, title, body, $"/orders", NotificationAudience.Customer, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RefundResponse> MapAsync(Refund refund, CancellationToken cancellationToken)
    {
        var orderNumber = await orders.Query().AsNoTracking()
            .Where(o => o.Id == refund.OrderId)
            .Select(o => o.OrderNumber)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? string.Empty;

        return new RefundResponse(
            refund.Id,
            refund.OrderId,
            orderNumber,
            refund.PaymentId,
            refund.Status,
            refund.Amount,
            refund.Reason,
            refund.Description,
            refund.ReviewNote,
            refund.RejectionReason,
            refund.RequestedAt,
            refund.ReviewedAt,
            refund.CompletedAt,
            refund.Items.Select(i => new RefundItemResponse(i.Id, i.OrderItemId, i.ProductId, i.ProductName, i.Sku, i.Quantity, i.Amount)).ToList());
    }
}
