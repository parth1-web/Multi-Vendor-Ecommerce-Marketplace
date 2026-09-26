using Marketplace.API.Middleware;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Catalog.Abstractions;
using Marketplace.Application.Modules.Catalog.DTOs;
using Marketplace.Application.Modules.Reviews.DTOs;
using Marketplace.Application.Modules.Sellers.Abstractions;
using Marketplace.Application.Modules.Sellers.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.API.Controllers;

/// <summary>Public catalogue endpoints. No authentication, cache-friendly, indexable.</summary>
[ApiController]
[Route("api/products")]
[AllowAnonymous]
public sealed class ProductsController(IProductService products) : ControllerBase
{
    /// <summary>Lists published products with search, filters, sorting and pagination.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProductSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        [FromQuery] Guid? categoryId,
        [FromQuery] string? categorySlug,
        [FromQuery] Guid? sellerId,
        [FromQuery] string? sellerSlug,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] decimal? minRating,
        [FromQuery] bool? inStock,
        [FromQuery] bool? onSale,
        [FromQuery] bool? includeSubcategories,
        [FromQuery] string? sort,
        CancellationToken cancellationToken)
    {
        var query = new ProductQuery(
            page, pageSize, search, categoryId, categorySlug, sellerId, sellerSlug,
            minPrice, maxPrice, minRating, inStock, onSale, null, ParseSort(sort),
            includeSubcategories ?? true);

        return Ok(await products.ListAsync(query, cancellationToken));
    }

    [HttpGet("featured")]
    [ProducesResponseType(typeof(IReadOnlyList<ProductSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Featured([FromQuery] int take = 8, CancellationToken cancellationToken = default) =>
        Ok(await products.GetFeaturedAsync(take, cancellationToken));

    [HttpGet("best-sellers")]
    [ProducesResponseType(typeof(IReadOnlyList<ProductSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> BestSellers([FromQuery] int take = 8, CancellationToken cancellationToken = default) =>
        Ok(await products.GetBestSellersAsync(take, cancellationToken));

    [HttpGet("new-arrivals")]
    [ProducesResponseType(typeof(IReadOnlyList<ProductSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> NewArrivals([FromQuery] int take = 8, CancellationToken cancellationToken = default) =>
        Ok(await products.GetNewArrivalsAsync(take, cancellationToken));

    [HttpGet("slug/{slug}")]
    [ProducesResponseType(typeof(ProductDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken cancellationToken) =>
        (await products.GetBySlugAsync(slug, cancellationToken)).ToActionResult();

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProductDetailResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        (await products.GetByIdAsync(id, cancellationToken)).ToActionResult();

    private static ProductSortOption ParseSort(string? sort) => sort?.Trim().ToLowerInvariant() switch
    {
        "price_asc" or "priceasc" or "price" => ProductSortOption.PriceAsc,
        "price_desc" or "pricedesc" => ProductSortOption.PriceDesc,
        "rating" => ProductSortOption.Rating,
        "popular" or "popularity" => ProductSortOption.Popular,
        "name_asc" or "nameasc" => ProductSortOption.NameAsc,
        "name_desc" or "namedesc" => ProductSortOption.NameDesc,
        "discount" => ProductSortOption.Discount,
        _ => ProductSortOption.Newest
    };
}

/// <summary>Category tree. Read is public; writes are admin-only.</summary>
[ApiController]
[Route("api/categories")]
public sealed class CategoriesController(ICategoryService categories) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Tree([FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
        Ok(await categories.GetTreeAsync(!includeInactive, cancellationToken));

    [HttpGet("{idOrSlug}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(string idOrSlug, CancellationToken cancellationToken) =>
        (await categories.GetByIdOrSlugAsync(idOrSlug, cancellationToken)).ToActionResult();

    [HttpPost]
    [Authorize(Policy = Security.AuthorizationPolicies.AdminOnly)]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var result = await categories.CreateAsync(request, cancellationToken);
        return result.ToActionResult(category => CreatedAtAction(nameof(Get), new { idOrSlug = category.Slug }, category));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Security.AuthorizationPolicies.AdminOnly)]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCategoryRequest request, CancellationToken cancellationToken) =>
        (await categories.UpdateAsync(id, request, cancellationToken)).ToActionResult();

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Security.AuthorizationPolicies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await categories.DeleteAsync(id, cancellationToken)).ToActionResult();

    [HttpPut("reorder")]
    [Authorize(Policy = Security.AuthorizationPolicies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reorder([FromBody] ReorderCategoriesRequest request, CancellationToken cancellationToken) =>
        (await categories.ReorderAsync(request.Items, cancellationToken)).ToActionResult();
}

/// <summary>Public store profile pages.</summary>
[ApiController]
[Route("api/stores")]
[AllowAnonymous]
public sealed class StoresController(ISellerService sellers) : ControllerBase
{
    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(StoreProfileResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBySlug(string slug, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        (await sellers.GetStoreBySlugAsync(slug, new PageRequest(page, pageSize), cancellationToken)).ToActionResult();
}

/// <summary>Seller and store management.</summary>
[ApiController]
[Route("api/sellers")]
public sealed class SellersController(ISellerService sellers, Marketplace.Application.Common.Interfaces.ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("me")]
    [Authorize(Policy = Security.AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType(typeof(SellerResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken) =>
        (await sellers.GetCurrentAsync(cancellationToken)).ToActionResult();

    [HttpGet]
    [Authorize(Policy = Security.AuthorizationPolicies.AdminOnly)]
    [ProducesResponseType(typeof(PagedResult<SellerListItemResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? status,
        [FromQuery] string? search,
        CancellationToken cancellationToken) =>
        Ok(await sellers.ListAsync(
            new SellerListQuery(page, pageSize, ParseStatus(status), search),
            cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Security.AuthorizationPolicies.SellerOrAdmin)]
    [ProducesResponseType(typeof(SellerResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        (await sellers.GetByIdAsync(id, cancellationToken)).ToActionResult();

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Security.AuthorizationPolicies.SellerOrAdmin)]
    [ProducesResponseType(typeof(SellerResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSellerRequest request, CancellationToken cancellationToken) =>
        (await sellers.UpdateAsync(id, request, cancellationToken)).ToActionResult();

    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = Security.AuthorizationPolicies.AdminOnly)]
    [ProducesResponseType(typeof(SellerResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] UpdateSellerStatusRequest request, CancellationToken cancellationToken)
    {
        var adminId = currentUser.UserId;
        return (await sellers.ChangeStatusAsync(id, request, adminId, cancellationToken)).ToActionResult();
    }

    [HttpGet("me/store")]
    [Authorize(Policy = Security.AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType(typeof(StoreProfileResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOwnStore(CancellationToken cancellationToken) =>
        (await sellers.GetOwnStoreAsync(cancellationToken)).ToActionResult();

    [HttpPut("me/store")]
    [Authorize(Policy = Security.AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType(typeof(StoreProfileResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateOwnStore([FromBody] UpdateStoreRequest request, CancellationToken cancellationToken) =>
        (await sellers.UpdateOwnStoreAsync(request, cancellationToken)).ToActionResult();

    private static Domain.Enums.SellerStatus? ParseStatus(string? status) =>
        Enum.TryParse<Domain.Enums.SellerStatus>(status, ignoreCase: true, out var parsed) ? parsed : null;
}
