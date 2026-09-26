using Marketplace.Application.Common.Extensions;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Reviews.DTOs;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Events;
using Marketplace.Domain.Identity;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Reviews;
using Marketplace.Domain.Sellers;
using Microsoft.EntityFrameworkCore;
using OrderEntity = Marketplace.Domain.Orders.Order;

namespace Marketplace.Application.Modules.Reviews.Abstractions;

public interface IReviewService
{
    Task<PagedResult<ReviewResponse>> ListForProductAsync(Guid productId, ReviewListQuery query, CancellationToken cancellationToken = default);

    Task<PagedResult<ReviewResponse>> ListForSellerAsync(ReviewListQuery query, CancellationToken cancellationToken = default);

    Task<Result<ReviewResponse>> CreateAsync(Guid productId, CreateReviewRequest request, CancellationToken cancellationToken = default);

    Task<Result<ReviewResponse>> UpdateAsync(Guid reviewId, UpdateReviewRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid reviewId, CancellationToken cancellationToken = default);

    Task<Result<ReviewResponse>> ModerateAsync(Guid reviewId, ModerateReviewRequest request, CancellationToken cancellationToken = default);

    Task<Result<ReviewReplyResponse>> ReplyAsync(Guid reviewId, ReplyToReviewRequest request, CancellationToken cancellationToken = default);

    Task<Result> MarkHelpfulAsync(Guid reviewId, CancellationToken cancellationToken = default);
}

public sealed record ReviewListQuery(int? Page, int? PageSize, int? MinRating, bool? VisibleOnly, string Sort = "newest");
