using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Orders.DTOs;
using Marketplace.Domain.Enums;

namespace Marketplace.Application.Modules.Orders.Abstractions;

public interface ICheckoutService
{
    /// <summary>Prices a basket without writing anything. Used by the checkout wizard's quote step.</summary>
    Task<Result<QuoteResponse>> QuoteAsync(QuoteRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates, reserves inventory, creates the marketplace order, splits it into
    /// seller sub-orders and creates the payment — all inside one database transaction.
    /// </summary>
    Task<Result<CheckoutResponse>> CheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default);
}

public interface IOrderService
{
    Task<PagedResult<OrderListItemResponse>> ListOwnAsync(OrderListQuery query, CancellationToken cancellationToken = default);

    Task<Result<OrderResponse>> GetOwnAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<Result> CancelOwnAsync(Guid orderId, CancelOrderRequest request, CancellationToken cancellationToken = default);

    Task<PagedResult<SellerOrderSummaryResponse>> ListSellerOrdersAsync(OrderListQuery query, CancellationToken cancellationToken = default);

    Task<Result<SellerOrderDetailResponse>> GetSellerOrderAsync(Guid sellerOrderId, CancellationToken cancellationToken = default);

    Task<Result> UpdateSellerOrderStatusAsync(Guid sellerOrderId, UpdateOrderStatusRequest request, CancellationToken cancellationToken = default);

    Task<PagedResult<OrderListItemResponse>> ListAllAsync(OrderListQuery query, CancellationToken cancellationToken = default);

    Task<Result<OrderResponse>> GetByIdForAdminAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<Result> UpdateStatusAsAdminAsync(Guid orderId, OrderStatus status, string? note, CancellationToken cancellationToken = default);
}

public interface IAddressService
{
    Task<IReadOnlyList<AddressResponse>> ListAsync(CancellationToken cancellationToken = default);

    Task<Result<AddressResponse>> CreateAsync(CreateAddressRequest request, CancellationToken cancellationToken = default);

    Task<Result<AddressResponse>> UpdateAsync(Guid id, UpdateAddressRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> SetDefaultAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record OrderListQuery(
    int? Page,
    int? PageSize,
    OrderStatus? Status,
    string? Search,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string Sort = "newest");
