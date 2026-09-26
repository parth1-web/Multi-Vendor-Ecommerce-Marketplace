using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Orders;

/// <summary>
/// The single legal source of order state transitions. Kept in the Domain so a
/// controller, a background job and an admin action all obey exactly the same rules.
/// </summary>
public static class OrderStatusTransition
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Allowed = new()
    {
        [OrderStatus.Pending] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Processing, OrderStatus.Cancelled],
        [OrderStatus.Processing] = [OrderStatus.Packed, OrderStatus.Cancelled],
        [OrderStatus.Packed] = [OrderStatus.Shipped],
        [OrderStatus.Shipped] = [OrderStatus.Delivered],
        [OrderStatus.Delivered] = [OrderStatus.Returned, OrderStatus.Completed],
        [OrderStatus.Cancelled] = [],
        [OrderStatus.Returned] = [],
        [OrderStatus.Completed] = []
    };

    private static readonly Dictionary<SellerOrderStatus, SellerOrderStatus[]> AllowedSeller = new()
    {
        [SellerOrderStatus.Pending] = [SellerOrderStatus.Confirmed, SellerOrderStatus.Cancelled],
        [SellerOrderStatus.Confirmed] = [SellerOrderStatus.Processing, SellerOrderStatus.Cancelled],
        [SellerOrderStatus.Processing] = [SellerOrderStatus.Packed, SellerOrderStatus.Cancelled],
        [SellerOrderStatus.Packed] = [SellerOrderStatus.Shipped],
        [SellerOrderStatus.Shipped] = [SellerOrderStatus.Delivered],
        [SellerOrderStatus.Delivered] = [SellerOrderStatus.Returned, SellerOrderStatus.Completed],
        [SellerOrderStatus.Cancelled] = [],
        [SellerOrderStatus.Returned] = [],
        [SellerOrderStatus.Completed] = []
    };

    public static IReadOnlyCollection<OrderStatus> NextStates(OrderStatus current) => Allowed[current];

    public static IReadOnlyCollection<SellerOrderStatus> NextStates(SellerOrderStatus current) => AllowedSeller[current];

    public static bool IsAllowed(OrderStatus from, OrderStatus to) => Allowed.TryGetValue(from, out var next) && next.Contains(to);

    public static bool IsAllowed(SellerOrderStatus from, SellerOrderStatus to) => AllowedSeller.TryGetValue(from, out var next) && next.Contains(to);

    public static bool IsTerminal(OrderStatus status) => Allowed[status].Length == 0;

    public static bool IsTerminal(SellerOrderStatus status) => AllowedSeller[status].Length == 0;

    public static void EnsureCanTransition(OrderStatus from, OrderStatus to)
    {
        if (!IsAllowed(from, to))
        {
            throw new InvalidStateTransitionException("Order", from.ToString(), to.ToString());
        }
    }

    public static void EnsureCanTransition(SellerOrderStatus from, SellerOrderStatus to)
    {
        if (!IsAllowed(from, to))
        {
            throw new InvalidStateTransitionException("SellerOrder", from.ToString(), to.ToString());
        }
    }

    /// <summary>Customers may only cancel before the goods leave the building.</summary>
    public static bool IsCancellableByCustomer(OrderStatus status) =>
        status is OrderStatus.Pending or OrderStatus.Confirmed or OrderStatus.Processing;

    /// <summary>Only a delivered order can be returned.</summary>
    public static bool IsReturnable(OrderStatus status) => status == OrderStatus.Delivered;
}
