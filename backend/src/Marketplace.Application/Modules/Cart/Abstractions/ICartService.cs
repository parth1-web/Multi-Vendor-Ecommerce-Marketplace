using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Orders.DTOs;

namespace Marketplace.Application.Modules.Cart.Abstractions;

public interface ICartService
{
    Task<Result<CartResponse>> GetAsync(string? guestToken, CancellationToken cancellationToken = default);

    Task<Result<CartResponse>> AddItemAsync(AddCartItemRequest request, string? guestToken, CancellationToken cancellationToken = default);

    Task<Result<CartResponse>> UpdateItemAsync(Guid itemId, UpdateCartItemRequest request, string? guestToken, CancellationToken cancellationToken = default);

    Task<Result<CartResponse>> RemoveItemAsync(Guid itemId, string? guestToken, CancellationToken cancellationToken = default);

    Task<Result<CartResponse>> ToggleSavedForLaterAsync(Guid itemId, string? guestToken, CancellationToken cancellationToken = default);

    Task<Result<CartResponse>> ClearAsync(string? guestToken, CancellationToken cancellationToken = default);

    Task<Result<CartResponse>> MergeGuestCartAsync(string guestToken, CancellationToken cancellationToken = default);
}

public interface IWishlistService
{
    Task<Result<IReadOnlyList<WishlistItemResponse>>> GetAsync(CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<WishlistItemResponse>>> AddAsync(Guid productId, CancellationToken cancellationToken = default);

    Task<Result> RemoveAsync(Guid productId, CancellationToken cancellationToken = default);

    Task<Result> ClearAsync(CancellationToken cancellationToken = default);
}

public sealed record WishlistItemResponse(
    Guid ProductId,
    string Name,
    string Slug,
    decimal Price,
    decimal? CompareAtPrice,
    int DiscountPercentage,
    string? ImageUrl,
    decimal RatingAverage,
    int RatingCount,
    bool IsInStock,
    string StoreName,
    string StoreSlug,
    DateTimeOffset AddedAt);
