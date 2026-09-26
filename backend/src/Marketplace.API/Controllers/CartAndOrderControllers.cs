using Marketplace.API.Middleware;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Cart.Abstractions;
using Marketplace.Application.Modules.Orders.Abstractions;
using Marketplace.Application.Modules.Orders.DTOs;
using Marketplace.Application.Modules.Catalog.DTOs;
using Marketplace.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.API.Controllers;

/// <summary>
/// Cart endpoints. Anonymous access is allowed and bound to an opaque guest token that
/// the browser keeps in a cookie; the same endpoints serve signed-in customers.
/// </summary>
[ApiController]
[Route("api/cart")]
[AllowAnonymous]
public sealed class CartController(ICartService cart) : ControllerBase
{
    private const string GuestCookie = "mp_cart";

    [HttpGet]
    [ProducesResponseType(typeof(CartResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        (await cart.GetAsync(ReadGuestToken(), cancellationToken)).ToActionResult();

    [HttpPost("items")]
    [ProducesResponseType(typeof(CartResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddItem([FromBody] AddCartItemRequest request, CancellationToken cancellationToken)
    {
        var result = await cart.AddItemAsync(request, ReadGuestToken(), cancellationToken);
        if (result.IsSuccess && result.Value!.CartId != Guid.Empty && User.Identity?.IsAuthenticated != true)
        {
            EnsureGuestCookie();
        }

        return result.ToActionResult();
    }

    [HttpPut("items/{itemId:guid}")]
    [ProducesResponseType(typeof(CartResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateItem(Guid itemId, [FromBody] UpdateCartItemRequest request, CancellationToken cancellationToken) =>
        (await cart.UpdateItemAsync(itemId, request, ReadGuestToken(), cancellationToken)).ToActionResult();

    [HttpDelete("items/{itemId:guid}")]
    [ProducesResponseType(typeof(CartResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveItem(Guid itemId, CancellationToken cancellationToken) =>
        (await cart.RemoveItemAsync(itemId, ReadGuestToken(), cancellationToken)).ToActionResult();

    [HttpPut("items/{itemId:guid}/save-for-later")]
    [ProducesResponseType(typeof(CartResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveForLater(Guid itemId, CancellationToken cancellationToken) =>
        (await cart.ToggleSavedForLaterAsync(itemId, ReadGuestToken(), cancellationToken)).ToActionResult();

    [HttpDelete]
    [ProducesResponseType(typeof(CartResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken) =>
        (await cart.ClearAsync(ReadGuestToken(), cancellationToken)).ToActionResult();

    [HttpPost("merge")]
    [Authorize]
    [ProducesResponseType(typeof(CartResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Merge(CancellationToken cancellationToken)
    {
        var token = ReadGuestToken();
        return string.IsNullOrWhiteSpace(token)
            ? (await cart.GetAsync(null, cancellationToken)).ToActionResult()
            : (await cart.MergeGuestCartAsync(token, cancellationToken)).ToActionResult();
    }

    private string? ReadGuestToken() => Request.Cookies[GuestCookie];

    private void EnsureGuestCookie()
    {
        if (string.IsNullOrWhiteSpace(ReadGuestToken()))
        {
            var token = Guid.NewGuid().ToString("N");
            Response.Cookies.Append(GuestCookie, token, new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/api/cart",
                Expires = DateTimeOffset.UtcNow.AddDays(60)
            });
        }
    }
}

/// <summary>Checkout: quote then place the order.</summary>
[ApiController]
[Route("api/checkout")]
[Authorize]
public sealed class CheckoutController(ICheckoutService checkout) : ControllerBase
{
    /// <summary>Prices the current basket without persisting anything.</summary>
    [HttpPost("quote")]
    [ProducesResponseType(typeof(QuoteResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Quote([FromBody] QuoteRequest request, CancellationToken cancellationToken) =>
        (await checkout.QuoteAsync(request, cancellationToken)).ToActionResult();

    /// <summary>
    /// Validates, reserves stock, creates the marketplace order, splits it per seller and
    /// creates the payment. Accepts an <c>Idempotency-Key</c> header for safe retries.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CheckoutResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Place([FromBody] CheckoutRequest request, CancellationToken cancellationToken)
    {
        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault() ?? request.IdempotencyKey;
        var effective = request with { IdempotencyKey = idempotencyKey };

        var result = await checkout.CheckoutAsync(effective, cancellationToken);
        return result.ToActionResult(response =>
        {
            Response.Headers["Idempotency-Key"] = idempotencyKey ?? string.Empty;
            return StatusCode(StatusCodes.Status201Created, response);
        });
    }
}

/// <summary>Customer order history, detail, cancellation and the address book.</summary>
[ApiController]
[Route("api/orders")]
[Authorize]
public sealed class OrdersController(IOrderService orders) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<OrderListItemResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? status,
        [FromQuery] string? search, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] string? sort, CancellationToken cancellationToken) =>
        Ok(await orders.ListOwnAsync(new OrderListQuery(page, pageSize, ParseStatus(status), search, from, to, sort ?? "newest"), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        (await orders.GetOwnAsync(id, cancellationToken)).ToActionResult();

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelOrderRequest request, CancellationToken cancellationToken) =>
        (await orders.CancelOwnAsync(id, request, cancellationToken)).ToActionResult();

    private static OrderStatus? ParseStatus(string? status) =>
        Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsed) ? parsed : null;
}

/// <summary>Customer address book. Every operation is scoped to the caller's user id.</summary>
[ApiController]
[Route("api/addresses")]
[Authorize]
public sealed class AddressesController(IAddressService addresses) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AddressResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) => Ok(await addresses.ListAsync(cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(AddressResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateAddressRequest request, CancellationToken cancellationToken) =>
        (await addresses.CreateAsync(request, cancellationToken)).ToActionResult(address => StatusCode(StatusCodes.Status201Created, address));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AddressResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAddressRequest request, CancellationToken cancellationToken) =>
        (await addresses.UpdateAsync(id, request, cancellationToken)).ToActionResult();

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await addresses.DeleteAsync(id, cancellationToken)).ToActionResult();

    [HttpPut("{id:guid}/default")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetDefault(Guid id, CancellationToken cancellationToken) =>
        (await addresses.SetDefaultAsync(id, cancellationToken)).ToActionResult();
}

/// <summary>Saved products.</summary>
[ApiController]
[Route("api/wishlist")]
[Authorize]
public sealed class WishlistController(IWishlistService wishlist) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<WishlistItemResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await wishlist.GetAsync(cancellationToken)).ToActionResult();

    [HttpPost]
    [ProducesResponseType(typeof(IReadOnlyList<WishlistItemResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Add([FromBody] AddWishlistItemRequest request, CancellationToken cancellationToken) =>
        (await wishlist.AddAsync(request.ProductId, cancellationToken)).ToActionResult();

    [HttpDelete("{productId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove(Guid productId, CancellationToken cancellationToken) =>
        (await wishlist.RemoveAsync(productId, cancellationToken)).ToActionResult();

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken) =>
        (await wishlist.ClearAsync(cancellationToken)).ToActionResult();
}
