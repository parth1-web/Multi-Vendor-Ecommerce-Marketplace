using Marketplace.Application.Common.Extensions;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Auth.Abstractions;
using Marketplace.Application.Modules.Inventory.Abstractions;
using Marketplace.Application.Modules.Payments.Abstractions;
using Marketplace.Application.Modules.Payments.DTOs;
using Marketplace.Domain.Commissions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Events;
using Marketplace.Domain.Inventory;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderEntity = Marketplace.Domain.Orders.Order;

namespace Marketplace.Application.Modules.Payments.Services;

/// <summary>
/// Payments. Settlement happens only through a server-side verification or a signed
/// webhook — the browser's success callback is cosmetic. Webhooks are signature-checked
/// and idempotent, so replaying one is a harmless no-op.
/// </summary>
public sealed class PaymentService(
    IRepository<Payment> payments,
    IRepository<PaymentWebhook> webhooks,
    IRepository<OrderEntity> orders,
    IRepository<Commission> commissions,
    IRepository<InventoryReservation> reservations,
    IInventoryService inventory,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    IClock clock,
    IAuditService auditService,
    INotificationService notificationService,
    IRealtimeNotifier realtime,
    IPaymentGatewayResolver gateways,
    IOptions<MarketplaceOptions> marketplaceOptions,
    IOptions<PaymentOptions> paymentOptions,
    ILogger<PaymentService> logger) : IPaymentService
{
    private readonly MarketplaceOptions _marketplace = marketplaceOptions.Value;
    private readonly PaymentOptions _payment = paymentOptions.Value;

    public async Task<Result<PaymentResponse>> CreateAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Result<PaymentResponse>.Failure("Sign in to pay for an order.");
        }

        var order = await orders.Query()
            .Include(o => o.SellerOrders)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId && o.CustomerId == currentUser.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return Result<PaymentResponse>.Failure("Order not found.", ResultErrorCodes.NotFound);
        }

        if (order.IsPaid)
        {
            return Result<PaymentResponse>.Failure("This order has already been paid.");
        }

        // Idempotency: the same key must never create a second payment for one order.
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var replay = await payments.Query().AsNoTracking()
                .Include(p => p.Transactions)
                .FirstOrDefaultAsync(p => p.OrderId == order.Id && p.IdempotencyKey == request.IdempotencyKey, cancellationToken)
                .ConfigureAwait(false);

            if (replay is not null)
            {
                return Result<PaymentResponse>.Success(await MapAsync(replay, cancellationToken).ConfigureAwait(false));
            }
        }

        var now = clock.UtcNow;
        var provider = _payment.DefaultProvider;
        var payment = Payment.Create(order.Id, order.CustomerId, provider, order.TotalAmount, order.Currency, request.IdempotencyKey, now);
        await payments.AddAsync(payment, cancellationToken).ConfigureAwait(false);

        var gateway = gateways.Resolve(provider);
        var initiation = await gateway.CreatePaymentAsync(new PaymentInitiationRequest(
            payment.Id, order.Id, order.OrderNumber, order.TotalAmount, order.Currency,
            currentUser.Email ?? string.Empty, order.OrderNumber,
            $"{_marketplace.FrontendBaseUrl}/checkout?order={order.OrderNumber}",
            $"{_marketplace.FrontendBaseUrl}/checkout?failed={order.OrderNumber}",
            $"{_marketplace.FrontendBaseUrl}/orders",
            null), cancellationToken).ConfigureAwait(false);

        if (!initiation.IsSuccess)
        {
            payment.MarkFailed(
                initiation.FailureReason ?? "The payment could not be started.",
                PaymentTransaction.Create(payment.Id, PaymentTransactionType.GatewayResponse, null, initiation.RawResponse, false, initiation.FailureReason, now),
                now);

            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result<PaymentResponse>.Failure(initiation.FailureReason ?? "The payment could not be started.");
        }

        payment.AttachGatewayResponse(
            initiation.GatewayPaymentId,
            initiation.RedirectUrl,
            PaymentTransaction.Create(payment.Id, PaymentTransactionType.GatewayResponse, null, initiation.RawResponse, true, null, now),
            now);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.PaymentCreated, nameof(Payment), payment.Id, payment.TransactionReference,
            new { order.OrderNumber, payment.Amount, Provider = provider.ToString() }, cancellationToken).ConfigureAwait(false);

        return Result<PaymentResponse>.Success(await MapAsync(payment, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<PaymentResponse>> GetAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        var payment = await payments.Query()
            .Include(p => p.Transactions)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paymentId && (currentUser.IsAdmin || p.CustomerId == currentUser.UserId), cancellationToken)
            .ConfigureAwait(false);

        if (payment is null)
        {
            return Result<PaymentResponse>.Failure("Payment not found.", ResultErrorCodes.NotFound);
        }

        return Result<PaymentResponse>.Success(await MapAsync(payment, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<PaymentResponse>> VerifyAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        var payment = await payments.Query()
            .Include(p => p.Transactions)
            .FirstOrDefaultAsync(p => p.Id == paymentId && (currentUser.IsAdmin || p.CustomerId == currentUser.UserId), cancellationToken)
            .ConfigureAwait(false);

        if (payment is null)
        {
            return Result<PaymentResponse>.Failure("Payment not found.", ResultErrorCodes.NotFound);
        }

        if (payment.IsSettled)
        {
            // Verification is naturally idempotent.
            return Result<PaymentResponse>.Success(await MapAsync(payment, cancellationToken).ConfigureAwait(false));
        }

        var gateway = gateways.Resolve(payment.Provider);
        var result = await gateway.VerifyAsync(
            new PaymentVerificationRequest(payment.Id, payment.GatewayPaymentId, payment.Amount, payment.Currency),
            cancellationToken).ConfigureAwait(false);

        var now = clock.UtcNow;
        var transaction = PaymentTransaction.Create(
            payment.Id, PaymentTransactionType.Verification, null, result.RawResponse, result.IsVerified, result.FailureReason, now);

        if (!result.IsVerified)
        {
            payment.MarkFailed(result.FailureReason ?? "The payment could not be verified.", transaction, now);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await realtime.PaymentUpdatedAsync(payment.Id, payment.OrderId, payment.Status.ToString(), cancellationToken).ConfigureAwait(false);
            return Result<PaymentResponse>.Failure(result.FailureReason ?? "The payment could not be verified.");
        }

        payment.MarkSucceeded(result.GatewayReference, transaction, now);
        await SettleOrderAsync(payment, now, cancellationToken).ConfigureAwait(false);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.PaymentVerified, nameof(Payment), payment.Id, payment.TransactionReference,
            new { Amount = payment.Amount, Status = payment.Status.ToString() }, cancellationToken).ConfigureAwait(false);

        await NotifyAsync(payment, true, null, cancellationToken).ConfigureAwait(false);
        await realtime.PaymentUpdatedAsync(payment.Id, payment.OrderId, payment.Status.ToString(), cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Payment {Reference} verified and settled", payment.TransactionReference);
        return Result<PaymentResponse>.Success(await MapAsync(payment, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<PaymentResponse>> HandleWebhookAsync(WebhookEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var now = clock.UtcNow;

        // 1. Idempotency — the provider event id is unique in the database.
        var alreadySeen = await webhooks.Query().AsNoTracking()
            .FirstOrDefaultAsync(w => w.Provider == envelope.Provider && w.ProviderEventId == envelope.ProviderEventId, cancellationToken)
            .ConfigureAwait(false);

        if (alreadySeen is not null)
        {
            logger.LogInformation("Duplicate webhook {EventId} ignored", envelope.ProviderEventId);
            if (alreadySeen.PaymentId is { } seenPaymentId)
            {
                var seen = await payments.Query()
                    .Include(p => p.Transactions)
                    .FirstOrDefaultAsync(p => p.Id == seenPaymentId, cancellationToken)
                    .ConfigureAwait(false);

                if (seen is not null)
                {
                    return Result<PaymentResponse>.Success(await MapAsync(seen, cancellationToken).ConfigureAwait(false));
                }
            }

            return Result<PaymentResponse>.Failure("Duplicate event.");
        }

        // 2. Signature — verified over the raw body in constant time.
        var provider = Enum.TryParse<PaymentProvider>(envelope.Provider, ignoreCase: true, out var parsedProvider)
            ? parsedProvider
            : PaymentProvider.Mock;

        var gateway = gateways.Resolve(provider);
        var signatureValid = gateway.VerifySignature(envelope.RawBody, envelope.Signature, _payment.WebhookSecret);

        var record = PaymentWebhook.Create(null, envelope.Provider, envelope.ProviderEventId, envelope.Signature, envelope.RawBody, envelope.EventType, now);
        await webhooks.AddAsync(record, cancellationToken).ConfigureAwait(false);

        if (!signatureValid)
        {
            record.MarkFailed("invalid-signature", now);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await auditService.RecordAsync(AuditAction.WebhookReceived, nameof(PaymentWebhook), record.Id, envelope.ProviderEventId,
                new { Valid = false }, cancellationToken).ConfigureAwait(false);
            logger.LogWarning("Rejected webhook {EventId} with an invalid signature", envelope.ProviderEventId);
            return Result<PaymentResponse>.Failure("Invalid signature.");
        }

        record.MarkSignatureValid(now);

        // 3. Locate the payment and cross-check amount and order.
        var payment = await payments.Query()
            .Include(p => p.Transactions)
            .FirstOrDefaultAsync(p => p.GatewayPaymentId == envelope.GatewayPaymentId ||
                                      (envelope.OrderNumber != null && p.OrderId == orders.Query().Where(o => o.OrderNumber == envelope.OrderNumber).Select(o => o.Id).FirstOrDefault()),
                cancellationToken)
            .ConfigureAwait(false);

        if (payment is null)
        {
            record.MarkFailed("payment-not-found", now);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result<PaymentResponse>.Failure("Payment not found for this event.", ResultErrorCodes.NotFound);
        }

        record.MarkProcessed(payment.Id, now);
        payment.RecordTransaction(PaymentTransaction.Create(payment.Id, PaymentTransactionType.WebhookReceived, envelope.RawBody, null, true, null, now), now);

        if (envelope.Amount is { } amount && Math.Round(amount, 2) != Math.Round(payment.Amount, 2))
        {
            record.MarkFailed("amount-mismatch", now);
            payment.MarkFailed("Webhook amount does not match the payment.",
                PaymentTransaction.Create(payment.Id, PaymentTransactionType.WebhookProcessed, envelope.RawBody, null, false, "amount-mismatch", now), now);

            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogWarning("Rejected webhook {EventId}: amount mismatch", envelope.ProviderEventId);
            return Result<PaymentResponse>.Failure("Amount mismatch.");
        }

        if (string.Equals(envelope.EventType, "failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(envelope.EventType, "payment.failed", StringComparison.OrdinalIgnoreCase))
        {
            payment.MarkFailed("The payment was declined by the provider.",
                PaymentTransaction.Create(payment.Id, PaymentTransactionType.WebhookProcessed, envelope.RawBody, null, false, "declined", now), now);

            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await NotifyAsync(payment, false, "declined", cancellationToken).ConfigureAwait(false);
            await realtime.PaymentUpdatedAsync(payment.Id, payment.OrderId, payment.Status.ToString(), cancellationToken).ConfigureAwait(false);
            return Result<PaymentResponse>.Success(await MapAsync(payment, cancellationToken).ConfigureAwait(false));
        }

        if (payment.IsSettled)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result<PaymentResponse>.Success(await MapAsync(payment, cancellationToken).ConfigureAwait(false));
        }

        payment.MarkSucceeded(payment.GatewayPaymentId,
            PaymentTransaction.Create(payment.Id, PaymentTransactionType.WebhookProcessed, envelope.RawBody, null, true, null, now), now);

        await SettleOrderAsync(payment, now, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditService.RecordAsync(AuditAction.WebhookReceived, nameof(PaymentWebhook), record.Id, envelope.ProviderEventId,
            new { Valid = true, payment.Id }, cancellationToken).ConfigureAwait(false);

        await NotifyAsync(payment, true, null, cancellationToken).ConfigureAwait(false);
        await realtime.PaymentUpdatedAsync(payment.Id, payment.OrderId, payment.Status.ToString(), cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Webhook {EventId} settled payment {Reference}", envelope.ProviderEventId, payment.TransactionReference);
        return Result<PaymentResponse>.Success(await MapAsync(payment, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// The caller's own payments, newest first.
    /// </summary>
    /// <remarks>
    /// A customer has to be able to see what they were charged and what state it is in, so
    /// the scope comes from the token rather than from a parameter a caller could widen.
    /// </remarks>
    public async Task<IReadOnlyList<PaymentResponse>> ListOwnAsync(CancellationToken cancellationToken = default)
    {
        var owned = await payments.Query().AsNoTracking()
            .Include(p => p.Transactions)
            .Where(p => p.CustomerId == currentUser.UserId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var results = new List<PaymentResponse>(owned.Count);
        foreach (var payment in owned)
        {
            results.Add(await MapAsync(payment, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    public async Task<PagedResult<PaymentResponse>> ListAllAsync(PaymentListQuery query, CancellationToken cancellationToken = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var source = payments.Query().AsNoTracking();

        if (query.Status is { } status)
        {
            source = source.Where(p => p.Status == status);
        }

        if (query.OrderId is { } orderId)
        {
            source = source.Where(p => p.OrderId == orderId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            source = source.Where(p => EF.Functions.Like(p.TransactionReference, term));
        }

        var result = await source
            .OrderByDescending(p => p.CreatedAt)
            .ToPagedResultAsync(page, p => new PaymentResponse(
                p.Id, p.OrderId, string.Empty, p.Provider, p.Status, p.Amount, p.Currency,
                p.TransactionReference, p.GatewayPaymentId, p.GatewayRedirectUrl, null, p.FailureReason,
                false, p.InitiatedAt, p.CompletedAt, []), cancellationToken)
            .ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Settles the marketplace order for a successful payment: marks the order paid,
    /// converts reservations into sales and creates one commission per seller sub-order.
    /// </summary>
    private async Task SettleOrderAsync(Payment payment, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var order = await orders.Query()
            .Include(o => o.SellerOrders)
            .FirstOrDefaultAsync(o => o.Id == payment.OrderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return;
        }

        order.MarkPaid(now);

        // Convert the holds taken at checkout into completed sales.
        var held = await reservations.Query()
            .Where(r => r.OrderId == order.Id && r.ReleasedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var reservation in held)
        {
            await inventory.CommitReservationAsync(reservation, cancellationToken).ConfigureAwait(false);
        }

        // One commission per seller sub-order, from its own net amount.
        foreach (var sellerOrder in order.SellerOrders)
        {
            var alreadyExists = await commissions.AnyAsync(c => c.SellerOrderId == sellerOrder.Id, cancellationToken).ConfigureAwait(false);
            if (alreadyExists)
            {
                continue;
            }

            var net = decimal.Round(sellerOrder.Subtotal - sellerOrder.DiscountAmount, 2);
            var commission = Commission.Create(sellerOrder.Id, order.Id, sellerOrder.SellerId, sellerOrder.CommissionRate, net, order.Currency, now);
            commission.Accrue(now);
            commission.AddDomainEvent(new CommissionCreatedEvent(commission.Id, sellerOrder.Id, sellerOrder.SellerId, commission.CommissionAmount, commission.SellerAmount, now));

            await commissions.AddAsync(commission, cancellationToken).ConfigureAwait(false);
        }

        order.AddDomainEvent(new PaymentSucceededEvent(payment.Id, order.Id, order.CustomerId, payment.Amount, payment.TransactionReference, now));
    }

    private async Task NotifyAsync(Payment payment, bool succeeded, string? reason, CancellationToken cancellationToken)
    {
        var order = await orders.Query().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == payment.OrderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return;
        }

        await notificationService.NotifyAsync(
            order.CustomerId,
            succeeded ? NotificationType.PaymentSuccessful : NotificationType.PaymentFailed,
            succeeded ? "Payment received" : "Payment failed",
            succeeded
                ? $"We received your payment for order {order.OrderNumber}."
                : $"The payment for order {order.OrderNumber} could not be completed. {reason}".Trim(),
            $"/orders/{order.OrderNumber}",
            NotificationAudience.Customer,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<PaymentResponse> MapAsync(Payment payment, CancellationToken cancellationToken)
    {
        var order = await orders.Query().AsNoTracking()
            .Where(o => o.Id == payment.OrderId)
            .Select(o => o.OrderNumber)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var gateway = gateways.Resolve(payment.Provider);

        return new PaymentResponse(
            payment.Id,
            payment.OrderId,
            order ?? string.Empty,
            payment.Provider,
            payment.Status,
            payment.Amount,
            payment.Currency,
            payment.TransactionReference,
            payment.GatewayPaymentId,
            payment.GatewayRedirectUrl,
            null,
            payment.FailureReason,
            gateway.RequiresCustomerAction && !payment.IsSettled,
            payment.InitiatedAt,
            payment.CompletedAt,
            payment.Transactions
                .OrderBy(t => t.CreatedAt)
                .Select(t => new PaymentTransactionResponse(t.Id, t.Type, t.IsSuccess, t.ErrorMessage, t.CreatedAt))
                .ToList());
    }
}