using FluentAssertions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Orders;
using Xunit;

namespace Marketplace.UnitTests.Orders;

public sealed class OrderStatusTransitionTests
{
    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Processing)]
    [InlineData(OrderStatus.Processing, OrderStatus.Packed)]
    [InlineData(OrderStatus.Packed, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Completed)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Returned)]
    public void The_happy_path_is_allowed(OrderStatus from, OrderStatus to)
    {
        OrderStatusTransition.IsAllowed(from, to).Should().BeTrue();
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Pending, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Packed, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Completed, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Returned, OrderStatus.Delivered)]
    public void Illegal_transitions_are_refused(OrderStatus from, OrderStatus to)
    {
        OrderStatusTransition.IsAllowed(from, to).Should().BeFalse();

        var act = () => OrderStatusTransition.EnsureCanTransition(from, to);
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Theory]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Returned)]
    public void Terminal_states_have_no_way_out(OrderStatus status)
    {
        OrderStatusTransition.IsTerminal(status).Should().BeTrue();
        OrderStatusTransition.NextStates(status).Should().BeEmpty();
    }

    [Fact]
    public void Cancel_is_only_allowed_before_dispatch()
    {
        OrderStatusTransition.IsCancellableByCustomer(OrderStatus.Pending).Should().BeTrue();
        OrderStatusTransition.IsCancellableByCustomer(OrderStatus.Confirmed).Should().BeTrue();
        OrderStatusTransition.IsCancellableByCustomer(OrderStatus.Processing).Should().BeTrue();

        OrderStatusTransition.IsCancellableByCustomer(OrderStatus.Packed).Should().BeFalse();
        OrderStatusTransition.IsCancellableByCustomer(OrderStatus.Shipped).Should().BeFalse();
        OrderStatusTransition.IsCancellableByCustomer(OrderStatus.Delivered).Should().BeFalse();
    }

    [Fact]
    public void Only_a_delivered_order_can_be_returned()
    {
        OrderStatusTransition.IsReturnable(OrderStatus.Delivered).Should().BeTrue();
        OrderStatusTransition.IsReturnable(OrderStatus.Shipped).Should().BeFalse();
    }

    [Fact]
    public void Seller_order_transitions_mirror_the_marketplace_rules()
    {
        OrderStatusTransition.IsAllowed(SellerOrderStatus.Pending, SellerOrderStatus.Confirmed).Should().BeTrue();
        OrderStatusTransition.IsAllowed(SellerOrderStatus.Pending, SellerOrderStatus.Shipped).Should().BeFalse();

        var act = () => OrderStatusTransition.EnsureCanTransition(SellerOrderStatus.Completed, SellerOrderStatus.Cancelled);
        act.Should().Throw<InvalidStateTransitionException>();
    }
}

public sealed class SellerOrderSplitTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void One_sub_order_is_created_per_seller()
    {
        var sellerA = Guid.NewGuid();
        var sellerB = Guid.NewGuid();
        var order = CreateOrder();

        order.AddSellerOrder(SellerOrder.Create(order.Id, sellerA, "MP-1-01", 100m, 0m, 0m, 0m, 10m, Now));
        order.AddSellerOrder(SellerOrder.Create(order.Id, sellerB, "MP-1-02", 50m, 0m, 0m, 0m, 10m, Now));

        order.SellerOrders.Should().HaveCount(2);
        order.SellerOrders.Select(so => so.SellerId).Should().BeEquivalentTo([sellerA, sellerB]);
    }

    [Fact]
    public void Sub_order_totals_sum_to_the_marketplace_total()
    {
        var order = CreateOrder();
        var a = SellerOrder.Create(order.Id, Guid.NewGuid(), "MP-1-01", 100m, 0m, 0m, 0m, 10m, Now);
        var b = SellerOrder.Create(order.Id, Guid.NewGuid(), "MP-1-02", 50m, 0m, 0m, 0m, 12m, Now);

        order.AddSellerOrder(a);
        order.AddSellerOrder(b);

        (a.TotalAmount + b.TotalAmount).Should().Be(order.TotalAmount);
    }

    [Fact]
    public void Each_sub_order_snapshots_its_own_commission_rate()
    {
        var order = CreateOrder();
        var a = SellerOrder.Create(order.Id, Guid.NewGuid(), "MP-1-01", 10_000m, 0m, 0m, 0m, 10m, Now);
        var b = SellerOrder.Create(order.Id, Guid.NewGuid(), "MP-1-02", 10_000m, 0m, 0m, 0m, 15m, Now);

        a.CommissionAmount.Should().Be(1_000m);
        b.CommissionAmount.Should().Be(1_500m);
        a.SellerEarnings.Should().Be(9_000m);
        b.SellerEarnings.Should().Be(8_500m);
    }

    [Fact]
    public void The_marketplace_order_completes_only_when_every_sub_order_has_completed()
    {
        var order = CreateOrder();
        var a = SellerOrder.Create(order.Id, Guid.NewGuid(), "MP-1-01", 100m, 0m, 0m, 0m, 10m, Now);
        var b = SellerOrder.Create(order.Id, Guid.NewGuid(), "MP-1-02", 50m, 0m, 0m, 0m, 10m, Now);
        order.AddSellerOrder(a);
        order.AddSellerOrder(b);

        Walk(a, SellerOrderStatus.Delivered);
        order.SyncFromSellerOrder(a.Status, Now);
        order.Status.Should().Be(OrderStatus.Delivered);

        Walk(a, SellerOrderStatus.Completed);
        order.SyncFromSellerOrder(a.Status, Now);
        order.Status.Should().NotBe(OrderStatus.Completed);

        Walk(b, SellerOrderStatus.Completed);
        order.SyncFromSellerOrder(b.Status, Now);
        order.Status.Should().Be(OrderStatus.Completed);
    }

    [Fact]
    public void Cancelling_the_order_cancels_every_sub_order()
    {
        var order = CreateOrder();
        order.AddSellerOrder(SellerOrder.Create(order.Id, Guid.NewGuid(), "MP-1-01", 100m, 0m, 0m, 0m, 10m, Now));
        order.AddSellerOrder(SellerOrder.Create(order.Id, Guid.NewGuid(), "MP-1-02", 50m, 0m, 0m, 0m, 10m, Now));

        order.Cancel("changed my mind", null, Now);

        order.Status.Should().Be(OrderStatus.Cancelled);
        order.SellerOrders.Should().OnlyContain(so => so.Status == SellerOrderStatus.Cancelled);
    }

    [Fact]
    public void An_order_with_a_zero_total_cannot_be_placed()
    {
        var act = () => CreateOrder(subtotal: 0m);

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void A_discount_larger_than_the_subtotal_is_refused()
    {
        var address = new OrderAddressSnapshot("Home", "A", "1", "Street", null, "City", null, "1", "NP");

        var act = () => Order.Place(Guid.NewGuid(), address, null, null, 50m, 60m, 0m, 0m, "USD", "Mock", null, Now);

        act.Should().Throw<ValidationException>();
    }

    private static readonly SellerOrderStatus[] FulfilmentPath =
    [
        SellerOrderStatus.Confirmed, SellerOrderStatus.Processing, SellerOrderStatus.Packed,
        SellerOrderStatus.Shipped, SellerOrderStatus.Delivered, SellerOrderStatus.Completed
    ];

    /// <summary>Walks a sub-order along the fulfilment path to the requested state.</summary>
    private static void Walk(SellerOrder sellerOrder, SellerOrderStatus target)
    {
        var index = Array.IndexOf(FulfilmentPath, target);
        var path = index >= 0
            ? FulfilmentPath[..(index + 1)].ToList()
            : [.. FulfilmentPath, target];

        var current = path.IndexOf(sellerOrder.Status);

        foreach (var step in path.Skip(current < 0 ? 0 : current + 1))
        {
            sellerOrder.ChangeStatus(step, null, null, Now);
        }
    }

    private static Order CreateOrder(decimal subtotal = 150m)
    {
        var address = new OrderAddressSnapshot("Home", "Aarav Sharma", "+9779800000000", "12 Ratna Marg", null, "Kathmandu", "Bagmati", "44600", "NP");
        return Order.Place(Guid.NewGuid(), address, null, null, subtotal, 0m, 0m, 0m, "USD", "Mock", null, Now);
    }
}
