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

    /// <summary>
    /// Every review on the marketplace for moderation, hidden ones included.
    /// </summary>
    /// <remarks>
    /// The gap this exists to close: the only other reads of a review either belong to a single
    /// product or to a single seller, and both exclude hidden reviews on the shopper's behalf. A
    /// moderator therefore had no way to see what they had hidden, and no way to undo it.
    /// </remarks>
    Task<PagedResult<ModerationReviewResponse>> ListForModerationAsync(ReviewModerationQuery query, CancellationToken cancellationToken = default);

    /// <summary>One review for moderation, hidden or not.</summary>
    Task<Result<ModerationReviewResponse>> GetForModerationAsync(Guid reviewId, CancellationToken cancellationToken = default);

    Task<Result<ReviewResponse>> CreateAsync(Guid productId, CreateReviewRequest request, CancellationToken cancellationToken = default);

    Task<Result<ReviewResponse>> UpdateAsync(Guid reviewId, UpdateReviewRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid reviewId, CancellationToken cancellationToken = default);

    Task<Result<ReviewResponse>> ModerateAsync(Guid reviewId, ModerateReviewRequest request, CancellationToken cancellationToken = default);

    Task<Result<ReviewReplyResponse>> ReplyAsync(Guid reviewId, ReplyToReviewRequest request, CancellationToken cancellationToken = default);

    Task<Result> MarkHelpfulAsync(Guid reviewId, CancellationToken cancellationToken = default);
}

public sealed record ReviewListQuery(int? Page, int? PageSize, int? MinRating, bool? VisibleOnly, string Sort = "newest");

/// <summary>
/// The moderation list's filters. Each one maps to a real column or a real relation, and there is
/// no sort: the list is newest first because a moderator's question is "what has just arrived".
/// </summary>
public sealed record ReviewModerationQuery(
    int? Page,
    int? PageSize,
    bool? IsVisible,
    int? Rating,
    string? Search,
    Guid? ProductId);
