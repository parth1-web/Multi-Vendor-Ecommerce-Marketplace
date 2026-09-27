using Marketplace.Application.Modules.Reviews.DTOs;
using Marketplace.Domain.Enums;
namespace Marketplace.Application.Modules.Catalog.DTOs;

/// <summary>
/// A seller's own view of a product, in full.
///
/// The public detail is a shopper's view: it hides the price of a variant that is not for sale
/// and says nothing about moderation. Neither of those helps the person who has to fix the
/// listing, so the seller gets this instead.
/// </summary>
public sealed record SellerProductDetailResponse(
    Guid Id,
    string Name,
    string Slug,
    string ShortDescription,
    string Description,
    decimal BasePrice,
    decimal? CompareAtPrice,
    int DiscountPercentage,
    Guid CategoryId,
    string CategoryName,
    string? Brand,
    string? Model,
    ProductStatus Status,
    ProductRejectionReason RejectionReason,
    string? RejectionNote,
    bool IsFeatured,
    bool IsInStock,
    int AvailableQuantity,
    int SoldCount,
    int ViewCount,
    decimal RatingAverage,
    int RatingCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<ProductImageResponse> Images,
    IReadOnlyList<ProductVariantResponse> Variants,
    IReadOnlyList<ProductSpecificationResponse> Specifications,
    IReadOnlyList<string> Tags);

/// <summary>
/// A seller's own view of a product in a list, which is not the public one.
///
/// A seller has to be able to tell a draft from a live listing and to see why a moderator sent
/// it back. None of that belongs in the shape the catalogue publishes, and omitting it leaves the
/// seller's own list indistinguishable from a shopper's.
/// </summary>
public sealed record SellerProductListItemResponse(
    Guid Id,
    string Name,
    string Slug,
    string ShortDescription,
    decimal BasePrice,
    decimal? CompareAtPrice,
    int DiscountPercentage,
    string? PrimaryImageUrl,
    Guid CategoryId,
    string CategoryName,
    ProductStatus Status,
    ProductRejectionReason RejectionReason,
    string? RejectionNote,
    bool IsFeatured,
    bool IsInStock,
    int AvailableQuantity,
    int SoldCount,
    decimal RatingAverage,
    int RatingCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>The compact product shape used in listings, carts, orders and dashboards.</summary>

public sealed record ProductSummaryResponse(
    Guid Id,
    string Name,
    string Slug,
    string ShortDescription,
    decimal BasePrice,
    decimal? CompareAtPrice,
    int DiscountPercentage,
    string? PrimaryImageUrl,
    string? PrimaryImageAlt,
    Guid SellerId,
    string SellerName,
    string StoreName,
    string StoreSlug,
    Guid CategoryId,
    string CategoryName,
    string CategorySlug,
    decimal RatingAverage,
    int RatingCount,
    bool IsInStock,
    int AvailableQuantity,
    bool IsFeatured,
    bool IsNew,
    int SoldCount,
    DateTimeOffset CreatedAt);

/// <summary>Full product payload for the product detail page.</summary>
public sealed record ProductDetailResponse(
    Guid Id,
    string Name,
    string Slug,
    string ShortDescription,
    string Description,
    decimal BasePrice,
    decimal? CompareAtPrice,
    int DiscountPercentage,
    Guid SellerId,
    string SellerName,
    string StoreName,
    string StoreSlug,
    string? StoreLogoUrl,
    decimal StoreRating,
    int StoreRatingCount,
    Guid CategoryId,
    string CategoryName,
    string CategorySlug,
    string? Brand,
    string? Model,
    ProductStatus Status,
    decimal RatingAverage,
    int RatingCount,
    int ReviewCount,
    int SoldCount,
    int ViewCount,
    bool IsInStock,
    int AvailableQuantity,
    bool IsFeatured,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ProductImageResponse> Images,
    IReadOnlyList<ProductVariantResponse> Variants,
    IReadOnlyList<ProductSpecificationResponse> Specifications,
    IReadOnlyList<string> Tags,
    IReadOnlyList<RelatedProductResponse> RelatedProducts,
    IReadOnlyList<ReviewSummaryResponse> Reviews,
    RatingBreakdownResponse RatingBreakdown);

public sealed record ProductImageResponse(Guid Id, string Url, string? AltText, bool IsPrimary, int SortOrder);

public sealed record ProductVariantResponse(
    Guid Id,
    string Sku,
    string Name,
    decimal Price,
    bool IsActive,
    int AvailableQuantity,
    bool IsInStock,
    int LowStockThreshold,
    IReadOnlyList<ProductVariantOptionResponse> Options);

public sealed record ProductVariantOptionResponse(string Name, string Value);

public sealed record ProductSpecificationResponse(string Key, string Value, int SortOrder);

public sealed record RelatedProductResponse(
    Guid Id,
    string Name,
    string Slug,
    decimal BasePrice,
    decimal? CompareAtPrice,
    int DiscountPercentage,
    string? PrimaryImageUrl,
    decimal RatingAverage,
    int RatingCount,
    string StoreName,
    bool IsInStock);

public sealed record CategoryResponse(
    Guid Id,
    Guid? ParentId,
    string Name,
    string Slug,
    string? Description,
    string? ImageUrl,
    int DisplayOrder,
    bool IsActive,
    int ProductCount,
    IReadOnlyList<CategoryResponse> Children,
    IReadOnlyList<BreadcrumbItemResponse> Breadcrumb);

public sealed record CategoryTreeResponse(IReadOnlyList<CategoryResponse> Items);

public sealed record BreadcrumbItemResponse(string Name, string Slug, string Url);

public sealed record CreateProductRequest(
    string Name,
    string? Slug,
    string ShortDescription,
    string Description,
    Guid CategoryId,
    decimal BasePrice,
    decimal? CompareAtPrice,
    string? Brand,
    string? Model,
    IReadOnlyList<CreateProductImageRequest> Images,
    IReadOnlyList<CreateProductVariantRequest> Variants,
    IReadOnlyList<CreateProductSpecificationRequest> Specifications,
    IReadOnlyList<string> Tags);

public sealed record CreateProductImageRequest(string Url, string? AltText, bool IsPrimary);

public sealed record CreateProductVariantRequest(
    string Sku,
    string Name,
    decimal? Price,
    int InitialStock,
    int? LowStockThreshold,
    IReadOnlyList<CreateProductVariantOptionRequest> Options);

public sealed record CreateProductVariantOptionRequest(string Name, string Value);

public sealed record CreateProductSpecificationRequest(string Key, string Value);

public sealed record UpdateProductRequest(
    string Name,
    string? Slug,
    string ShortDescription,
    string Description,
    Guid CategoryId,
    decimal BasePrice,
    decimal? CompareAtPrice,
    string? Brand,
    string? Model,
    IReadOnlyList<string>? Tags);

public sealed record AddProductImageRequest(string Url, string? AltText, bool IsPrimary);

public sealed record AddProductVariantRequest(
    string Sku,
    string Name,
    decimal? Price,
    int InitialStock,
    int? LowStockThreshold,
    IReadOnlyList<CreateProductVariantOptionRequest> Options);

public sealed record UpdateProductVariantRequest(string Name, decimal? Price, bool IsActive);

public sealed record ProductApprovalRequest(bool Approve, ProductRejectionReason Reason = ProductRejectionReason.None, string? Note = null);

public sealed record CreateCategoryRequest(
    string Name,
    string? Slug,
    string? Description,
    string? ImageUrl,
    Guid? ParentId,
    int DisplayOrder);

public sealed record UpdateCategoryRequest(
    string Name,
    string? Slug,
    string? Description,
    string? ImageUrl,
    int DisplayOrder,
    bool IsActive);

public sealed record ReorderCategoriesRequest(IReadOnlyList<CategoryOrderRequest> Items);

public sealed record CategoryOrderRequest(Guid Id, int DisplayOrder);
