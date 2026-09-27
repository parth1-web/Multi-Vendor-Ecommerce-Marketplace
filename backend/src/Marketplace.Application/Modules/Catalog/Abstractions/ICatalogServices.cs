using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Catalog.DTOs;
using Marketplace.Domain.Enums;

namespace Marketplace.Application.Modules.Catalog.Abstractions;

public sealed record ProductQuery(
    int? Page,
    int? PageSize,
    string? Search,
    Guid? CategoryId,
    string? CategorySlug,
    Guid? SellerId,
    string? SellerSlug,
    decimal? MinPrice,
    decimal? MaxPrice,
    decimal? MinRating,
    bool? InStock,
    bool? OnSale,
    ProductStatus? Status,
    ProductSortOption Sort = ProductSortOption.Newest,
    bool IncludeSubcategories = true,
    bool IncludeUnpublished = false);

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryResponse>> GetTreeAsync(bool activeOnly, CancellationToken cancellationToken = default);

    Task<Result<CategoryResponse>> GetByIdOrSlugAsync(string idOrSlug, CancellationToken cancellationToken = default);

    Task<Result<CategoryResponse>> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default);

    Task<Result<CategoryResponse>> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> ReorderAsync(IReadOnlyList<CategoryOrderRequest> items, CancellationToken cancellationToken = default);
}

public interface IProductService
{
    Task<PagedResult<ProductSummaryResponse>> ListAsync(ProductQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// A seller's own catalogue, including drafts and whatever a moderator sent back.
    /// </summary>
    Task<PagedResult<SellerProductListItemResponse>> ListForSellerAsync(
        Guid sellerId,
        int? page,
        int? pageSize,
        string? search,
        ProductStatus? status,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductSummaryResponse>> GetFeaturedAsync(int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductSummaryResponse>> GetBestSellersAsync(int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductSummaryResponse>> GetNewArrivalsAsync(int take, CancellationToken cancellationToken = default);

    Task<Result<ProductDetailResponse>> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// A product by id, for callers who are entitled to see it: a published product for anyone,
    /// an unpublished one only for the seller who owns it or an admin. A draft read through a
    /// public endpoint is a seller's unpublished work on show.
    /// </summary>
    Task<Result<ProductDetailResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>A product by id, but only if it is published. Safe for an anonymous route.</summary>
    Task<Result<ProductDetailResponse>> GetPublishedByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>A seller's own product in full, including why a moderator sent it back.</summary>
    Task<Result<SellerProductDetailResponse>> GetForSellerAsync(Guid sellerId, Guid id, CancellationToken cancellationToken = default);

    Task<Result<ProductSummaryResponse>> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default);

    Task<Result<ProductSummaryResponse>> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<ProductImageResponse>> AddImageAsync(Guid productId, AddProductImageRequest request, CancellationToken cancellationToken = default);

    Task<Result> RemoveImageAsync(Guid productId, Guid imageId, CancellationToken cancellationToken = default);

    Task<Result<ProductVariantResponse>> AddVariantAsync(Guid productId, AddProductVariantRequest request, CancellationToken cancellationToken = default);

    Task<Result> RemoveVariantAsync(Guid productId, Guid variantId, CancellationToken cancellationToken = default);

    Task<Result> SubmitForApprovalAsync(Guid productId, CancellationToken cancellationToken = default);

    Task<Result> ReviewApprovalAsync(Guid productId, ProductApprovalRequest request, CancellationToken cancellationToken = default);

    Task<Result> SetFeaturedAsync(Guid productId, bool isFeatured, CancellationToken cancellationToken = default);
}
