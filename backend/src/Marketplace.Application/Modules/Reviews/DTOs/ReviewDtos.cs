using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Catalog.DTOs;
using Marketplace.Domain.Enums;

namespace Marketplace.Application.Modules.Reviews.DTOs;

public sealed record ReviewResponse(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSlug,
    string? ProductImageUrl,
    int Rating,
    string Title,
    string Body,
    bool IsVerifiedPurchase,
    bool IsVisible,
    int HelpfulCount,
    string AuthorName,
    DateTimeOffset CreatedAt,
    ReviewReplyResponse? Reply);

public sealed record ReviewReplyResponse(Guid Id, string Body, string SellerName, DateTimeOffset CreatedAt);

public sealed record CreateReviewRequest(int Rating, string Title, string Body, Guid OrderItemId);

public sealed record UpdateReviewRequest(int Rating, string Title, string Body);

public sealed record ModerateReviewRequest(bool IsVisible, string? Note);

/// <summary>
/// One review as a moderator sees it.
/// </summary>
/// <remarks>
/// A dedicated shape rather than the shopper-facing <see cref="ReviewResponse"/>, for three
/// reasons. Moderation needs the timestamps the public DTO does not carry, it needs the store a
/// review belongs to in order to judge it in context, and it needs to be able to return a review
/// the moderator has hidden — which the public reads deliberately cannot do. Nothing here is
/// customer identity beyond a masked first name: a moderator decides whether content may be shown,
/// which needs no more than that.
/// </remarks>
public sealed record ModerationReviewResponse(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSlug,
    string? ProductImageUrl,
    Guid SellerId,
    string StoreName,
    int Rating,
    string Title,
    string Body,
    bool IsVerifiedPurchase,
    bool IsVisible,
    int HelpfulCount,
    string AuthorName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ModerationNote,
    ReviewReplyResponse? Reply);

public sealed record ReplyToReviewRequest(string Body);

public sealed record RatingBreakdownResponse(
    decimal Average,
    int Total,
    int FiveStar,
    int FourStar,
    int ThreeStar,
    int TwoStar,
    int OneStar);

public sealed record ReviewSummaryResponse(
    Guid Id,
    int Rating,
    string Title,
    string Body,
    string AuthorName,
    bool IsVerifiedPurchase,
    int HelpfulCount,
    DateTimeOffset CreatedAt,
    ReviewReplyResponse? Reply);
