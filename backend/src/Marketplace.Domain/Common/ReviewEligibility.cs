using Marketplace.Domain.Coupons;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Orders;

namespace Marketplace.Domain.Common;

/// <summary>
/// Decides whether a customer may review an order item. Centralising the rule means the
/// API, the seller dashboard and the tests all agree.
/// </summary>
public static class ReviewEligibility
{
    /// <summary>Number of days after delivery during which a review can still be left.</summary>
    public const int ReviewWindowDays = 30;

    public static ReviewEligibilityResult Evaluate(
        OrderItem? orderItem,
        Order? order,
        Guid customerId,
        bool reviewAlreadyExists,
        DateTimeOffset now)
    {
        if (orderItem is null)
        {
            return ReviewEligibilityResult.NotEligible("The order item does not exist.");
        }

        if (order is null)
        {
            return ReviewEligibilityResult.NotEligible("The order does not exist.");
        }

        if (order.CustomerId != customerId)
        {
            return ReviewEligibilityResult.NotEligible("Only the customer who placed the order can review it.");
        }

        if (order.Status is not (OrderStatus.Delivered or OrderStatus.Completed))
        {
            return ReviewEligibilityResult.NotEligible("Only a delivered order can be reviewed.");
        }

        if (reviewAlreadyExists || orderItem.IsReviewed)
        {
            return ReviewEligibilityResult.NotEligible("This purchase has already been reviewed.");
        }

        if (orderItem.Quantity < 1)
        {
            return ReviewEligibilityResult.NotEligible("The order line is empty.");
        }

        if (order.DeliveredAt is not null && now > order.DeliveredAt.Value.AddDays(ReviewWindowDays))
        {
            return ReviewEligibilityResult.NotEligible($"The {ReviewWindowDays}-day review window has closed.");
        }

        return ReviewEligibilityResult.Eligible();
    }
}

/// <summary>Outcome of a review-eligibility check, carrying a human-readable reason when refused.</summary>
public readonly record struct ReviewEligibilityResult(bool CanReview, string? Reason)
{
    public static ReviewEligibilityResult Eligible() => new(true, null);

    public static ReviewEligibilityResult NotEligible(string reason) => new(false, reason);
}
