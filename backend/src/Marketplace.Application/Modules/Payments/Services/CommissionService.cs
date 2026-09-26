using Marketplace.Application.Common.Extensions;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Payments.Abstractions;
using Marketplace.Application.Modules.Payments.DTOs;
using Marketplace.Domain.Commissions;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Sellers;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Modules.Payments.Services;

/// <summary>
/// Commission statements and payouts. Sellers only ever see their own ledger — the
/// predicate comes from the token's seller id, never from a query parameter.
/// </summary>
public sealed class CommissionService(
    IRepository<Commission> commissions,
    IRepository<SellerPayout> payouts,
    IRepository<SellerOrder> sellerOrders,
    ICurrentUser currentUser) : ICommissionService
{
    public async Task<PagedResult<CommissionResponse>> ListOwnAsync(int? page, int? pageSize, CommissionStatus? status, CancellationToken cancellationToken = default)
    {
        if (currentUser.SellerId is not { } sellerId)
        {
            return PagedResult<CommissionResponse>.Empty(page ?? 1, pageSize ?? 20);
        }

        var paging = new PageRequest(page, pageSize);
        var source = commissions.Query().AsNoTracking().Where(c => c.SellerId == sellerId);

        if (status is { } s)
        {
            source = source.Where(c => c.Status == s);
        }

        return await source
            .OrderByDescending(c => c.CreatedAt)
            .ToPagedResultAsync(paging, c => new CommissionResponse(
                c.Id, c.SellerOrderId, string.Empty, c.SellerId, c.Rate, c.GrossAmount,
                c.CommissionAmount, c.SellerAmount, c.Currency, c.Status, c.CreatedAt, c.AccruedAt), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PagedResult<CommissionResponse>> ListAllAsync(int? page, int? pageSize, Guid? sellerId, CommissionStatus? status, CancellationToken cancellationToken = default)
    {
        var paging = new PageRequest(page, pageSize);
        var source = commissions.Query().AsNoTracking();

        if (sellerId is { } sid)
        {
            source = source.Where(c => c.SellerId == sid);
        }

        if (status is { } s)
        {
            source = source.Where(c => c.Status == s);
        }

        var numbers = sellerOrders.Query().AsNoTracking()
            .Where(so => so.SellerId == (sellerId ?? Guid.Empty))
            .ToDictionary(so => so.Id, so => so.SellerOrderNumber);

        var result = await source
            .OrderByDescending(c => c.CreatedAt)
            .ToPagedResultAsync(paging, c => new CommissionResponse(
                c.Id, c.SellerOrderId, string.Empty, c.SellerId, c.Rate, c.GrossAmount,
                c.CommissionAmount, c.SellerAmount, c.Currency, c.Status, c.CreatedAt, c.AccruedAt), cancellationToken)
            .ConfigureAwait(false);

        var enriched = result.Items
            .Select(item => item with { SellerOrderNumber = numbers.GetValueOrDefault(item.SellerOrderId, string.Empty) })
            .ToList();

        return new PagedResult<CommissionResponse>(enriched, result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<PagedResult<PayoutResponse>> ListPayoutsAsync(int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        if (currentUser.SellerId is not { } sellerId)
        {
            return PagedResult<PayoutResponse>.Empty(page ?? 1, pageSize ?? 20);
        }

        var paging = new PageRequest(page, pageSize);

        return await payouts.Query().AsNoTracking()
            .Where(p => p.SellerId == sellerId)
            .OrderByDescending(p => p.CreatedAt)
            .ToPagedResultAsync(paging, p => new PayoutResponse(
                p.Id, p.Reference, p.GrossAmount, p.CommissionAmount, p.NetAmount, p.CommissionCount,
                p.Status, p.FailureReason, p.TransactionReference, p.PeriodStart, p.PeriodEnd, p.CreatedAt, p.ProcessedAt), cancellationToken)
            .ConfigureAwait(false);
    }
}
