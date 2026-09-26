using Marketplace.Domain.Common;

namespace Marketplace.Domain.Reviews;

/// <summary>
/// A verified-purchase review. Eligibility (correct buyer, delivered order, one review
/// per item) is decided by <see cref="ReviewEligibility"/> and enforced by a unique index
/// on <c>order_item_id</c>.
/// </summary>
public class Review : Entity
{
    private Review()
    {
        Title = string.Empty;
        Body = string.Empty;
    }

    private Review(Guid id, Guid productId, Guid orderItemId, Guid orderId, Guid customerId, Guid sellerId, int rating, string title, string body, bool isVerifiedPurchase, DateTimeOffset now)
        : base(id)
    {
        ProductId = productId;
        OrderItemId = orderItemId;
        OrderId = orderId;
        CustomerId = customerId;
        SellerId = sellerId;
        Rating = rating;
        Title = title;
        Body = body;
        IsVerifiedPurchase = isVerifiedPurchase;
        IsVisible = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid ProductId { get; private set; }

    public Guid OrderItemId { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid SellerId { get; private set; }

    public int Rating { get; private set; }

    public string Title { get; private set; }

    public string Body { get; private set; }

    public bool IsVerifiedPurchase { get; private set; }

    public bool IsVisible { get; private set; }

    public string? ModerationNote { get; private set; }

    public int HelpfulCount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public ReviewReply? Reply { get; private set; }

    public static Review Create(Guid productId, Guid orderItemId, Guid orderId, Guid customerId, Guid sellerId, int rating, string title, string body, bool isVerifiedPurchase, DateTimeOffset now)
    {
        Guard.NotEmpty(productId, nameof(productId));
        Guard.NotEmpty(orderItemId, nameof(orderItemId));
        Guard.NotEmpty(orderId, nameof(orderId));
        Guard.NotEmpty(customerId, nameof(customerId));
        Guard.InRange(rating, 1, 5, nameof(rating));
        Guard.NotNullOrWhiteSpace(body, nameof(body));

        return new Review(
            SequentialGuid.New(now),
            productId,
            orderItemId,
            orderId,
            customerId,
            sellerId,
            rating,
            title?.Trim() ?? string.Empty,
            body.Trim(),
            isVerifiedPurchase,
            now);
    }

    public void Update(int rating, string title, string body, DateTimeOffset now)
    {
        Guard.InRange(rating, 1, 5, nameof(rating));
        Guard.NotNullOrWhiteSpace(body, nameof(body));

        Rating = rating;
        Title = title?.Trim() ?? string.Empty;
        Body = body.Trim();
        UpdatedAt = now;
    }

    public void SetVisibility(bool isVisible, string? moderationNote, DateTimeOffset now)
    {
        IsVisible = isVisible;
        ModerationNote = moderationNote?.Trim();
        UpdatedAt = now;
    }

    public void MarkHelpful()
    {
        HelpfulCount++;
    }

    public void AttachReply(ReviewReply reply, DateTimeOffset now)
    {
        if (Reply is not null)
        {
            throw new BusinessRuleException("A seller may only reply once per review.");
        }

        Reply = reply;
        UpdatedAt = now;
    }
}

public class ReviewReply : Entity
{
    private ReviewReply()
    {
        Body = string.Empty;
    }

    private ReviewReply(Guid id, Guid reviewId, Guid sellerId, Guid sellerUserId, string body, DateTimeOffset now) : base(id)
    {
        ReviewId = reviewId;
        SellerId = sellerId;
        SellerUserId = sellerUserId;
        Body = body;
        CreatedAt = now;
    }

    public Guid ReviewId { get; private set; }

    public Guid SellerId { get; private set; }

    public Guid SellerUserId { get; private set; }

    public string Body { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static ReviewReply Create(Guid reviewId, Guid sellerId, Guid sellerUserId, string body, DateTimeOffset now)
    {
        Guard.NotEmpty(reviewId, nameof(reviewId));
        Guard.NotEmpty(sellerId, nameof(sellerId));
        Guard.NotNullOrWhiteSpace(body, nameof(body));

        return new ReviewReply(SequentialGuid.New(now), reviewId, sellerId, sellerUserId, body.Trim(), now);
    }
}
