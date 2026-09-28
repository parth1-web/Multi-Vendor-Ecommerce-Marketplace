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
    decimal PaidEarnings,
    decimal CommissionPaid,
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
/// <summary>
/// One store as it appears in the public directory of stores.
/// </summary>
/// <remarks>
/// Not a <see cref="StoreProfileResponse"/>: a directory is twenty shops at a glance, and a
/// storefront is one shop in detail. Sending twenty sets of policies and twenty product pages to
/// draw twenty cards would be sending almost all of it to be thrown away.
/// </remarks>
public sealed record StoreDirectoryEntryResponse(
    Guid SellerId,
    Guid StoreId,
    string Name,
    string Slug,
    string? LogoUrl,
    string? BannerUrl,
    string? Description,
    int ProductCount,
    decimal RatingAverage,
    int RatingCount);