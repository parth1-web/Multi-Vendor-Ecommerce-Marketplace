using Marketplace.Application.Modules.Catalog.DTOs;
using Marketplace.Application.Modules.Sellers.DTOs;
using Marketplace.Domain.Enums;

namespace Marketplace.Application.Modules.Orders.DTOs;

public sealed record AddCartItemRequest(Guid ProductId, Guid ProductVariantId, int Quantity = 1);

public sealed record UpdateCartItemRequest(int Quantity);

public sealed record CartResponse(
    Guid CartId,
    int ItemCount,
    int TotalQuantity,
    decimal Subtotal,
    decimal EstimatedShipping,
    decimal EstimatedTotal,
    string Currency,
    IReadOnlyList<CartSellerGroupResponse> Groups,
    DateTimeOffset LastActivityAt);

public sealed record CartSellerGroupResponse(
    Guid SellerId,
    string SellerName,
    string StoreName,
    string StoreSlug,
    string? StoreLogoUrl,
    decimal Subtotal,
    IReadOnlyList<CartItemResponse> Items);

public sealed record CartItemResponse(
    Guid Id,
    Guid ProductId,
    Guid ProductVariantId,
    string ProductName,
    string ProductSlug,
    string? ProductImageUrl,
    string VariantName,
    string Sku,
    int Quantity,
    int AvailableQuantity,
    decimal UnitPrice,
    decimal LineTotal,
    bool SavedForLater,
    bool PriceChanged,
    bool IsInStock,
    string? PriceChangeNote);

public sealed record QuoteRequest(Guid? ShippingAddressId, string? CouponCode, string? ShippingMethod = null, bool UseDefaultAddress = true);

public sealed record QuoteResponse(
    decimal Subtotal,
    decimal DiscountAmount,
    decimal ShippingAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    string Currency,
    int ItemCount,
    IReadOnlyList<QuoteSellerLineResponse> SellerBreakdown,
    CouponValidationSummaryResponse? Coupon,
    string? Message);

public sealed record QuoteSellerLineResponse(
    Guid SellerId,
    string StoreName,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal ShippingAmount,
    decimal TotalAmount,
    decimal CommissionRate,
    decimal SellerEarnings,
    int ItemCount);

public sealed record CouponValidationSummaryResponse(string Code, decimal DiscountAmount, string Label);

public sealed record CheckoutRequest(
    Guid ShippingAddressId,
    string PaymentMethod,
    string? CouponCode,
    string? CustomerNote,
    string? ShippingMethod = null,
    string? IdempotencyKey = null);

public sealed record CheckoutResponse(
    Guid OrderId,
    string OrderNumber,
    decimal TotalAmount,
    string Currency,
    OrderStatus Status,
    int SellerCount,
    string PaymentMethod,
    string? PaymentRedirectUrl,
    bool PaymentRequiresAction,
    string? Message);

public sealed record OrderResponse(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    DateTimeOffset PlacedAt,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal ShippingAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    decimal RefundedAmount,
    string Currency,
    bool IsPaid,
    string PaymentMethod,
    string PaymentStatus,
    string? CouponCode,
    AddressSnapshotResponse ShippingAddress,
    string? CustomerNote,
    string? CancellationReason,
    int SellerCount,
    IReadOnlyList<OrderItemResponse> Items,
    IReadOnlyList<SellerOrderSummaryResponse> SellerOrders,
    IReadOnlyList<OrderTimelineStepResponse> Timeline);

public sealed record OrderListItemResponse(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    DateTimeOffset PlacedAt,
    decimal TotalAmount,
    string Currency,
    bool IsPaid,
    string? FirstProductName,
    string? FirstProductImageUrl,
    int ItemCount,
    int SellerCount,
    string? SellerNames);

public sealed record OrderItemResponse(
    Guid Id,
    Guid ProductId,
    Guid ProductVariantId,
    Guid SellerId,
    string StoreName,
    string ProductName,
    string? ProductImageUrl,
    string VariantName,
    string Sku,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    bool IsReviewed,
    bool CanReview,
    bool CanRefund);

public sealed record SellerOrderSummaryResponse(
    Guid Id,
    string SellerOrderNumber,
    Guid SellerId,
    Guid OrderId,
    string OrderNumber,
    string StoreName,
    SellerOrderStatus Status,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal ShippingAmount,
    decimal TotalAmount,
    decimal CommissionRate,
    decimal CommissionAmount,
    decimal SellerEarnings,
    string? CarrierName,
    string? TrackingNumber,
    DateTimeOffset? EstimatedDeliveryAt,
    int ItemCount);

public sealed record OrderTimelineStepResponse(string Step, string Label, DateTimeOffset? At, bool IsComplete, bool IsCurrent, string? Note);

public sealed record AddressSnapshotResponse(
    string Label,
    string RecipientName,
    string PhoneNumber,
    string Line1,
    string? Line2,
    string City,
    string? State,
    string PostalCode,
    string Country);

public sealed record UpdateOrderStatusRequest(SellerOrderStatus Status, string? Note, string? CarrierName, string? TrackingNumber, string? TrackingUrl, DateTimeOffset? EstimatedDeliveryAt);

public sealed record CancelOrderRequest(string Reason);

public sealed record CreateAddressRequest(
    string Label,
    string RecipientName,
    string PhoneNumber,
    string Line1,
    string? Line2,
    string City,
    string? State,
    string PostalCode,
    string Country,
    bool IsDefault = false);

public sealed record UpdateAddressRequest(
    string Label,
    string RecipientName,
    string PhoneNumber,
    string Line1,
    string? Line2,
    string City,
    string? State,
    string PostalCode,
    string Country);

public sealed record AddressResponse(
    Guid Id,
    string Label,
    string RecipientName,
    string PhoneNumber,
    string Line1,
    string? Line2,
    string City,
    string? State,
    string PostalCode,
    string Country,
    bool IsDefault,
    DateTimeOffset CreatedAt);
