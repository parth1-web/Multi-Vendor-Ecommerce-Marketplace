using Marketplace.Domain.Enums;

namespace Marketplace.Application.Modules.Payments.DTOs;

public sealed record CreatePaymentRequest(Guid OrderId, string? IdempotencyKey);

public sealed record PaymentResponse(
    Guid Id,
    Guid OrderId,
    string OrderNumber,
    PaymentProvider Provider,
    PaymentStatus Status,
    decimal Amount,
    string Currency,
    string TransactionReference,
    string? GatewayPaymentId,
    string? RedirectUrl,
    string? QrCodeData,
    string? FailureReason,
    bool RequiresAction,
    DateTimeOffset InitiatedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<PaymentTransactionResponse> Transactions);

public sealed record PaymentTransactionResponse(
    Guid Id,
    PaymentTransactionType Type,
    bool IsSuccess,
    string? ErrorMessage,
    DateTimeOffset CreatedAt);

public sealed record CreateRefundRequest(Guid OrderId, IReadOnlyList<Guid> OrderItemIds, string Reason, string? Description);

public enum ReviewRefundAction
{
    Approve = 0,
    Reject = 1,
    MarkProcessing = 2
}

public sealed record ReviewRefundRequest(ReviewRefundAction Action, string? Note);

public sealed record RefundResponse(
    Guid Id,
    Guid OrderId,
    string OrderNumber,
    Guid PaymentId,
    RefundStatus Status,
    decimal Amount,
    string Reason,
    string? Description,
    string? ReviewNote,
    string? RejectionReason,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<RefundItemResponse> Items);

public sealed record RefundItemResponse(
    Guid Id,
    Guid OrderItemId,
    Guid ProductId,
    string ProductName,
    string Sku,
    int Quantity,
    decimal Amount);

public sealed record CommissionResponse(
    Guid Id,
    Guid SellerOrderId,
    string SellerOrderNumber,
    Guid SellerId,
    decimal Rate,
    decimal GrossAmount,
    decimal CommissionAmount,
    decimal SellerAmount,
    string Currency,
    CommissionStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AccruedAt);

public sealed record PayoutResponse(
    Guid Id,
    string Reference,
    decimal GrossAmount,
    decimal CommissionAmount,
    decimal NetAmount,
    int CommissionCount,
    PayoutStatus Status,
    string? FailureReason,
    string? TransactionReference,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt);
