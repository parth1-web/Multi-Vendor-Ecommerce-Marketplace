using Marketplace.Application.Common.Extensions;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Catalog.DTOs;
using Marketplace.Application.Modules.Sellers.DTOs;
using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Events;
using Marketplace.Domain.Identity;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Sellers;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Modules.Sellers.Abstractions;

public interface ISellerService
{
    Task<Result<SellerResponse>> GetCurrentAsync(CancellationToken cancellationToken = default);

    Task<Result<SellerResponse>> GetByIdAsync(Guid sellerId, CancellationToken cancellationToken = default);

    Task<PagedResult<SellerListItemResponse>> ListAsync(SellerListQuery query, CancellationToken cancellationToken = default);

    Task<Result<SellerResponse>> UpdateAsync(Guid sellerId, UpdateSellerRequest request, CancellationToken cancellationToken = default);

    Task<Result<SellerResponse>> ChangeStatusAsync(Guid sellerId, UpdateSellerStatusRequest request, Guid adminUserId, CancellationToken cancellationToken = default);

    Task<Result<StoreProfileResponse>> GetStoreBySlugAsync(string slug, PageRequest page, CancellationToken cancellationToken = default);

    Task<Result<StoreProfileResponse>> GetOwnStoreAsync(CancellationToken cancellationToken = default);

    Task<Result<StoreProfileResponse>> UpdateOwnStoreAsync(UpdateStoreRequest request, CancellationToken cancellationToken = default);
}

public sealed record SellerListQuery(
    int? Page,
    int? PageSize,
    SellerStatus? Status,
    string? Search,
    string Sort = "newest");
