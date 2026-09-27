using Marketplace.API.Middleware;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Catalog.Abstractions;
using Marketplace.Application.Modules.Catalog.DTOs;
using Marketplace.Application.Modules.Coupons.Abstractions;
using Marketplace.Application.Modules.Coupons.DTOs;
using Marketplace.Application.Modules.Inventory.Abstractions;
using Marketplace.Application.Modules.Notifications.DTOs;
using Marketplace.Application.Modules.Orders.Abstractions;
using Marketplace.Application.Modules.Orders.DTOs;
using Marketplace.Application.Modules.Payments.Abstractions;
using Marketplace.Application.Modules.Reviews.Abstractions;
using Marketplace.Application.Modules.Reviews.DTOs;
using Marketplace.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.API.Controllers;

/// <summary>Seller product management. Ownership is enforced in the service layer.</summary>
[ApiController]
[Route("api/seller/products")]
[Authorize(Policy = Security.AuthorizationPolicies.SellerOnly)]
public sealed class SellerProductsController(IProductService products, Marketplace.Application.Common.Interfaces.ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<SellerProductListItemResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, [FromQuery] string? status, CancellationToken cancellationToken)
    {
        // The scope comes from the token, never from the query string: a seller who could pass
        // a seller id would be able to read another seller's drafts.
        if (currentUser.SellerId is not { } sellerId)
        {
            return Forbid();
        }

        // The seller's own list, not the catalogue's: a draft and a live listing look identical
        // in the public shape, and telling them apart is the whole job of this screen.
        return Ok(await products.ListForSellerAsync(
            sellerId,
            page,
            pageSize,
            search,
            Enum.TryParse<ProductStatus>(status, true, out var parsed) ? parsed : null,
            cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType(typeof(ProductSummaryResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest request, CancellationToken cancellationToken) =>
        (await products.CreateAsync(request, cancellationToken)).ToActionResult(p => StatusCode(StatusCodes.Status201Created, p));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ProductSummaryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductRequest request, CancellationToken cancellationToken) =>
        (await products.UpdateAsync(id, request, cancellationToken)).ToActionResult();

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await products.DeleteAsync(id, cancellationToken)).ToActionResult();

    [HttpPost("{id:guid}/images")]
    [ProducesResponseType(typeof(ProductImageResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddImage(Guid id, [FromBody] AddProductImageRequest request, CancellationToken cancellationToken) =>
        (await products.AddImageAsync(id, request, cancellationToken)).ToActionResult(i => StatusCode(StatusCodes.Status201Created, i));

    [HttpDelete("{id:guid}/images/{imageId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveImage(Guid id, Guid imageId, CancellationToken cancellationToken) =>
        (await products.RemoveImageAsync(id, imageId, cancellationToken)).ToActionResult();

    [HttpPost("{id:guid}/variants")]
    [ProducesResponseType(typeof(ProductVariantResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddVariant(Guid id, [FromBody] AddProductVariantRequest request, CancellationToken cancellationToken) =>
        (await products.AddVariantAsync(id, request, cancellationToken)).ToActionResult(v => StatusCode(StatusCodes.Status201Created, v));

    [HttpDelete("{id:guid}/variants/{variantId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveVariant(Guid id, Guid variantId, CancellationToken cancellationToken) =>
        (await products.RemoveVariantAsync(id, variantId, cancellationToken)).ToActionResult();

    [HttpPost("{id:guid}/submit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken) =>
        (await products.SubmitForApprovalAsync(id, cancellationToken)).ToActionResult();
}

/// <summary>Admin product moderation.</summary>
[ApiController]
[Route("api/admin/products")]
[Authorize(Policy = Security.AuthorizationPolicies.AdminOnly)]
public sealed class AdminProductsController(IProductService products) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProductSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, [FromQuery] string? status, CancellationToken cancellationToken) =>
        Ok(await products.ListAsync(new ProductQuery(
            Page: page,
            PageSize: pageSize,
            Search: search,
            CategoryId: null,
            CategorySlug: null,
            // An admin reviews the whole catalogue, so no seller scope is applied.
            SellerId: null,
            SellerSlug: null,
            MinPrice: null,
            MaxPrice: null,
            MinRating: null,
            InStock: null,
            OnSale: null,
            Status: Enum.TryParse<ProductStatus>(status, true, out var parsed) ? parsed : null), cancellationToken));

    [HttpPut("{id:guid}/approval")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Review(Guid id, [FromBody] ProductApprovalRequest request, CancellationToken cancellationToken) =>
        (await products.ReviewApprovalAsync(id, request, cancellationToken)).ToActionResult();

    [HttpPut("{id:guid}/featured")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetFeatured(Guid id, [FromQuery] bool value, CancellationToken cancellationToken) =>
        (await products.SetFeaturedAsync(id, value, cancellationToken)).ToActionResult();
}

/// <summary>Seller stock management.</summary>
[ApiController]
[Route("api/inventory")]
[Authorize(Policy = Security.AuthorizationPolicies.SellerOrAdmin)]
public sealed class InventoryController(IInventoryService inventory) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<InventoryItemResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search,
        [FromQuery] bool? lowStockOnly, [FromQuery] bool? outOfStockOnly, [FromQuery] Guid? productId,
        CancellationToken cancellationToken) =>
        Ok(await inventory.ListAsync(new InventoryListQuery(page, pageSize, search, lowStockOnly, outOfStockOnly, productId), cancellationToken));

    [HttpGet("low-stock")]
    [ProducesResponseType(typeof(IReadOnlyList<InventoryItemResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> LowStock(CancellationToken cancellationToken) =>
        Ok(await inventory.GetLowStockAsync(cancellationToken));

    [HttpPut("{variantId:guid}")]
    [ProducesResponseType(typeof(InventoryItemResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Adjust(Guid variantId, [FromBody] AdjustStockRequest request, CancellationToken cancellationToken) =>
        (await inventory.AdjustAsync(variantId, request, cancellationToken)).ToActionResult();

    [HttpPut("{variantId:guid}/threshold")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetThreshold(Guid variantId, [FromBody] SetThresholdRequest request, CancellationToken cancellationToken) =>
        (await inventory.SetThresholdAsync(variantId, request, cancellationToken)).ToActionResult();

    [HttpGet("{variantId:guid}/transactions")]
    [ProducesResponseType(typeof(IReadOnlyList<InventoryTransactionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Transactions(Guid variantId, [FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
        Ok(await inventory.GetTransactionsAsync(variantId, take, cancellationToken));
}

/// <summary>Seller sub-order management.</summary>
[ApiController]
[Route("api/seller/orders")]
[Authorize(Policy = Security.AuthorizationPolicies.SellerOnly)]
public sealed class SellerOrdersController(IOrderService orders) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<SellerOrderSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? status, [FromQuery] string? search, CancellationToken cancellationToken) =>
        Ok(await orders.ListSellerOrdersAsync(new OrderListQuery(page, pageSize, ParseStatus(status), search, null, null), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SellerOrderSummaryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        (await orders.GetSellerOrderAsync(id, cancellationToken)).ToActionResult();

    [HttpPut("{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateOrderStatusRequest request, CancellationToken cancellationToken) =>
        (await orders.UpdateSellerOrderStatusAsync(id, request, cancellationToken)).ToActionResult();

    private static OrderStatus? ParseStatus(string? status) =>
        Enum.TryParse<OrderStatus>(status, true, out var parsed) ? parsed : null;
}

/// <summary>Public and administrative order access.</summary>
[ApiController]
[Route("api/admin/orders")]
[Authorize(Policy = Security.AuthorizationPolicies.AdminOnly)]
public sealed class AdminOrdersController(IOrderService orders) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<OrderListItemResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? status, [FromQuery] string? search, CancellationToken cancellationToken) =>
        Ok(await orders.ListAllAsync(new OrderListQuery(page, pageSize,
            Enum.TryParse<OrderStatus>(status, true, out var parsed) ? parsed : null, search, null, null), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        (await orders.GetByIdForAdminAsync(id, cancellationToken)).ToActionResult();

    [HttpPut("{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] AdminOrderStatusRequest request, CancellationToken cancellationToken) =>
        (await orders.UpdateStatusAsAdminAsync(id, request.Status, request.Note, cancellationToken)).ToActionResult();
}

public sealed record AdminOrderStatusRequest(OrderStatus Status, string? Note);

/// <summary>Reviews: public reads, verified-purchase writes.</summary>
[ApiController]
[Route("api/products/{productId:guid}/reviews")]
[AllowAnonymous]
public sealed class ProductReviewsController(IReviewService reviews) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ReviewResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid productId, [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] int? minRating, [FromQuery] string? sort, CancellationToken cancellationToken) =>
        Ok(await reviews.ListForProductAsync(productId, new ReviewListQuery(page, pageSize, minRating, true, sort ?? "newest"), cancellationToken));

    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(ReviewResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(Guid productId, [FromBody] CreateReviewRequest request, CancellationToken cancellationToken) =>
        (await reviews.CreateAsync(productId, request, cancellationToken)).ToActionResult(r => StatusCode(StatusCodes.Status201Created, r));
}

[ApiController]
[Route("api/reviews")]
[Authorize]
public sealed class ReviewsController(IReviewService reviews) : ControllerBase
{
    [HttpGet("seller")]
    [Authorize(Policy = Security.AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType(typeof(PagedResult<ReviewResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListForSeller([FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] bool? visibleOnly, CancellationToken cancellationToken) =>
        Ok(await reviews.ListForSellerAsync(new ReviewListQuery(page, pageSize, null, visibleOnly), cancellationToken));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ReviewResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateReviewRequest request, CancellationToken cancellationToken) =>
        (await reviews.UpdateAsync(id, request, cancellationToken)).ToActionResult();

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await reviews.DeleteAsync(id, cancellationToken)).ToActionResult();

    [HttpPost("{id:guid}/reply")]
    [Authorize(Policy = Security.AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType(typeof(ReviewReplyResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Reply(Guid id, [FromBody] ReplyToReviewRequest request, CancellationToken cancellationToken) =>
        (await reviews.ReplyAsync(id, request, cancellationToken)).ToActionResult(r => StatusCode(StatusCodes.Status201Created, r));

    [HttpPut("{id:guid}/visibility")]
    [Authorize(Policy = Security.AuthorizationPolicies.AdminOnly)]
    [ProducesResponseType(typeof(ReviewResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Moderate(Guid id, [FromBody] ModerateReviewRequest request, CancellationToken cancellationToken) =>
        (await reviews.ModerateAsync(id, request, cancellationToken)).ToActionResult();
}

/// <summary>Coupon management and validation.</summary>
[ApiController]
[Route("api/coupons")]
[Authorize]
public sealed class CouponsController(ICouponService coupons) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Security.AuthorizationPolicies.SellerOrAdmin)]
    [ProducesResponseType(typeof(PagedResult<CouponResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? status, [FromQuery] string? search, CancellationToken cancellationToken) =>
        Ok(await coupons.ListAsync(new CouponListQuery(page, pageSize,
            Enum.TryParse<CouponStatus>(status, true, out var parsed) ? parsed : null, search, null), cancellationToken));

    [HttpGet("public")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<PublicCouponResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Public(CancellationToken cancellationToken) => Ok(await coupons.ListActiveAsync(cancellationToken));

    [HttpPost]
    [Authorize(Policy = Security.AuthorizationPolicies.SellerOrAdmin)]
    [ProducesResponseType(typeof(CouponResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateCouponRequest request, CancellationToken cancellationToken) =>
        (await coupons.CreateAsync(request, cancellationToken)).ToActionResult(c => StatusCode(StatusCodes.Status201Created, c));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Security.AuthorizationPolicies.SellerOrAdmin)]
    [ProducesResponseType(typeof(CouponResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCouponRequest request, CancellationToken cancellationToken) =>
        (await coupons.UpdateAsync(id, request, cancellationToken)).ToActionResult();

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Security.AuthorizationPolicies.SellerOrAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await coupons.DeleteAsync(id, cancellationToken)).ToActionResult();

    [HttpPost("validate")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CouponValidationResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Validate([FromBody] ValidateCouponRequest request, [FromQuery] decimal? subtotal, CancellationToken cancellationToken) =>
        (await coupons.ValidateAsync(request, subtotal, cancellationToken)).ToActionResult();
}
