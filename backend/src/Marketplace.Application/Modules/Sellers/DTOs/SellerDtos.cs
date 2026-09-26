using Marketplace.Application.Modules.Catalog.DTOs;
using Marketplace.Application.Common.Models;
using Marketplace.Domain.Enums;

namespace Marketplace.Application.Modules.Sellers.DTOs;

public sealed record ApplySellerRequest(
    string BusinessName,
    string? LegalName,
    string PhoneNumber,
    string Address,
    string? TaxIdentityNumber,
    string? BankAccountName,
    string? BankAccountNumber,
    string StoreName,
    string StoreDescription);

public sealed record UpdateSellerRequest(
    string BusinessName,
    string? LegalName,
    string? PhoneNumber,
    string? Address,
    string? TaxIdentityNumber,
    string? BankAccountName,
    string? BankAccountNumber);

public sealed record UpdateSellerStatusRequest(SellerStatus Status, string? Reason, decimal? CommissionRate);

public sealed record UpdateStoreRequest(
    string Name,
    string? Description,
    string? LogoUrl,
    string? BannerUrl,
    string? SupportEmail,
    string? SupportPhone,
    string? ReturnPolicy,
    string? ShippingPolicy,
    int? FoundedYear);

public sealed record SellerResponse(
    Guid Id,
    Guid UserId,
    SellerStatus Status,
    string BusinessName,
    string? LegalName,
    string? PhoneNumber,
    string? Address,
    string? TaxIdentityNumber,
    string? BankAccountName,
    string? BankAccountNumber,
    decimal DefaultCommissionRate,
    string? RejectionReason,
    string? SuspensionReason,
    DateTimeOffset AppliedAt,
    DateTimeOffset? ApprovedAt,
    int ProductCount,
    int SellerOrderCount,
    decimal TotalRevenue,
    decimal PendingEarnings,
    string? StoreName,
    string? StoreSlug,
    decimal StoreRating,
    int StoreRatingCount);

public sealed record SellerListItemResponse(
    Guid Id,
    Guid UserId,
    string BusinessName,
    string UserName,
    string Email,
    SellerStatus Status,
    decimal DefaultCommissionRate,
    int ProductCount,
    int SellerOrderCount,
    decimal TotalRevenue,
    string? StoreSlug,
    DateTimeOffset AppliedAt,
    DateTimeOffset? ApprovedAt);

public sealed record StoreProfileResponse(
    Guid SellerId,
    Guid StoreId,
    string Name,
    string Slug,
    string Description,
    string? LogoUrl,
    string? BannerUrl,
    string? SupportEmail,
    string? SupportPhone,
    string? ReturnPolicy,
    string? ShippingPolicy,
    int? FoundedYear,
    decimal RatingAverage,
    int RatingCount,
    int ProductCount,
    int TotalSalesCount,
    bool IsActive,
    DateTimeOffset CreatedAt,
    PagedResult<ProductSummaryResponse> Products);
