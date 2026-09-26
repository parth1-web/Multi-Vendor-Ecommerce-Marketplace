using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Payments.DTOs;
using Marketplace.Domain.Enums;

namespace Marketplace.Application.Modules.Payments.Abstractions;

public interface IPaymentService
{
    Task<Result<PaymentResponse>> CreateAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default);

    Task<Result<PaymentResponse>> GetAsync(Guid paymentId, CancellationToken cancellationToken = default);

    Task<Result<PaymentResponse>> VerifyAsync(Guid paymentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles a gateway callback. Signature verification and event-id idempotency both
    /// happen here — a duplicate event is acknowledged without re-applying anything.
    /// </summary>
    Task<Result<PaymentResponse>> HandleWebhookAsync(WebhookEnvelope envelope, CancellationToken cancellationToken = default);

    Task<PagedResult<PaymentResponse>> ListAllAsync(PaymentListQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// The caller's own payments, newest first. A customer has to be able to see what they were
    /// charged and what state it is in, so this is scoped by the token rather than by a query
    /// parameter the caller could widen.
    /// </summary>
    Task<IReadOnlyList<PaymentResponse>> ListOwnAsync(CancellationToken cancellationToken = default);
}

public interface IRefundService
{
    Task<Result<RefundResponse>> RequestAsync(CreateRefundRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RefundResponse>> ListOwnAsync(CancellationToken cancellationToken = default);

    Task<Result<RefundResponse>> GetAsync(Guid refundId, CancellationToken cancellationToken = default);

    Task<PagedResult<RefundResponse>> ListAllAsync(RefundListQuery query, CancellationToken cancellationToken = default);

    Task<Result<RefundResponse>> ReviewAsync(Guid refundId, ReviewRefundRequest request, CancellationToken cancellationToken = default);
}

public interface ICommissionService
{
    Task<PagedResult<CommissionResponse>> ListOwnAsync(int? page, int? pageSize, CommissionStatus? status, CancellationToken cancellationToken = default);

    Task<PagedResult<CommissionResponse>> ListAllAsync(int? page, int? pageSize, Guid? sellerId, CommissionStatus? status, CancellationToken cancellationToken = default);

    Task<PagedResult<PayoutResponse>> ListPayoutsAsync(int? page, int? pageSize, CancellationToken cancellationToken = default);
}

/// <summary>Raw webhook delivery: the provider name, its unique event id, signature and body.</summary>
public sealed record WebhookEnvelope(
    string Provider,
    string ProviderEventId,
    string? Signature,
    string EventType,
    string RawBody,
    string? GatewayPaymentId,
    decimal? Amount,
    string? Currency,
    string? OrderNumber);

public sealed record PaymentListQuery(int? Page, int? PageSize, PaymentStatus? Status, Guid? OrderId, string? Search);

public sealed record RefundListQuery(int? Page, int? PageSize, RefundStatus? Status, Guid? OrderId, string? Search);
