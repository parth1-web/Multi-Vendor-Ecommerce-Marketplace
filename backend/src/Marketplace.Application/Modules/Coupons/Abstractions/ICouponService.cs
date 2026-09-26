using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Coupons.DTOs;
using Marketplace.Domain.Coupons;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Sellers;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Modules.Coupons.Abstractions;

public interface ICouponService
{
    Task<PagedResult<CouponResponse>> ListAsync(CouponListQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PublicCouponResponse>> ListActiveAsync(CancellationToken cancellationToken = default);

    Task<Result<CouponResponse>> CreateAsync(CreateCouponRequest request, CancellationToken cancellationToken = default);

    Task<Result<CouponResponse>> UpdateAsync(Guid id, UpdateCouponRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<CouponValidationResponse>> ValidateAsync(ValidateCouponRequest request, decimal? basketSubtotal, CancellationToken cancellationToken = default);
}

public sealed record CouponListQuery(int? Page, int? PageSize, CouponStatus? Status, string? Search, bool? MineOnly);
