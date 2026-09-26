using FluentAssertions;
using Marketplace.Application.Modules.Auth.Services;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Modules.Payments;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Common;
using Marketplace.Domain.Identity;
using Marketplace.Domain.Orders;
using Xunit;

namespace Marketplace.UnitTests.Security;

public sealed class PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void The_same_password_never_produces_the_same_hash()
    {
        _hasher.Hash("Customer@123").Should().NotBe(_hasher.Hash("Customer@123"));
    }

    [Fact]
    public void A_correct_password_verifies()
    {
        var hash = _hasher.Hash("Customer@123");

        _hasher.Verify("Customer@123", hash).Should().BeTrue();
    }

    [Fact]
    public void An_incorrect_password_is_rejected()
    {
        var hash = _hasher.Hash("Customer@123");

        _hasher.Verify("customer@123", hash).Should().BeFalse();
    }

    [Fact]
    public void A_malformed_hash_is_rejected_rather_than_throwing()
    {
        _hasher.Verify("anything", "not-a-hash").Should().BeFalse();
        _hasher.Verify("anything", "pbkdf2-sha256$100000$###$###").Should().BeFalse();
        _hasher.Verify(string.Empty, string.Empty).Should().BeFalse();
    }

    [Fact]
    public void A_hash_with_fewer_iterations_needs_rehashing()
    {
        _hasher.NeedsRehash("pbkdf2-sha256$1000$c2FsdA==$aGFzaA==").Should().BeTrue();
        _hasher.NeedsRehash(_hasher.Hash("Customer@123")).Should().BeFalse();
    }
}

public sealed class RefreshTokenProtectorTests
{
    private readonly RefreshTokenProtector _protector = new();

    [Fact]
    public void A_generated_token_is_long_and_unique()
    {
        var first = RefreshTokenProtector.CreateRawToken();
        var second = RefreshTokenProtector.CreateRawToken();

        first.Should().NotBe(second);
        first.Length.Should().BeGreaterThan(40);
    }

    [Fact]
    public void The_raw_token_never_appears_in_its_own_digest()
    {
        var raw = RefreshTokenProtector.CreateRawToken();

        _protector.Protect(raw).Should().NotContain(raw);
    }

    [Fact]
    public void Matching_succeeds_and_non_matching_fails()
    {
        var raw = RefreshTokenProtector.CreateRawToken();
        var digest = _protector.Protect(raw);

        _protector.Matches(raw, digest).Should().BeTrue();
        _protector.Matches("other-token", digest).Should().BeFalse();
        // A malformed stored digest must be rejected, not throw.
        _protector.Matches(raw, "garbage").Should().BeFalse();
    }
}

public sealed class WebhookSignatureTests
{
    private const string Secret = "development-webhook-secret";
    private const string Body = """{"event_id":"evt_1","amount":100.00}""";

    [Fact]
    public void A_signature_computed_over_the_raw_body_verifies()
    {
        var signature = WebhookSignature.ComputeHmacSha256Hex(Body, Secret);

        WebhookSignature.Verify(Body, signature, Secret).Should().BeTrue();
    }

    [Fact]
    public void A_sha256_prefixed_signature_also_verifies()
    {
        var signature = WebhookSignature.ComputeHmacSha256Hex(Body, Secret);

        WebhookSignature.Verify(Body, $"sha256={signature}", Secret).Should().BeTrue();
    }

    [Fact]
    public void A_tampered_body_fails_verification()
    {
        var signature = WebhookSignature.ComputeHmacSha256Hex(Body, Secret);

        WebhookSignature.Verify(Body.Replace("100.00", "1.00"), signature, Secret).Should().BeFalse();
    }

    [Fact]
    public void A_missing_signature_fails_verification()
    {
        WebhookSignature.Verify(Body, null, Secret).Should().BeFalse();
        WebhookSignature.Verify(Body, string.Empty, Secret).Should().BeFalse();
    }

    [Fact]
    public void A_different_secret_fails_verification()
    {
        var signature = WebhookSignature.ComputeHmacSha256Hex(Body, "other-secret");

        WebhookSignature.Verify(Body, signature, Secret).Should().BeFalse();
    }

    [Fact]
    public void The_mock_gateway_verifies_its_own_signature()
    {
        var gateway = new MockPaymentGateway();
        var signature = MockPaymentGateway.Sign(Body, Secret);

        gateway.VerifySignature(Body, signature, Secret).Should().BeTrue();
        gateway.VerifySignature(Body, "nope", Secret).Should().BeFalse();
    }
}

public sealed class ReviewEligibilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid CustomerId = Guid.NewGuid();

    [Fact]
    public void A_delivered_order_item_can_be_reviewed()
    {
        var (item, order) = DeliveredOrder();

        var result = ReviewEligibility.Evaluate(item, order, CustomerId, false, Now);

        result.CanReview.Should().BeTrue();
    }

    [Fact]
    public void Another_customer_cannot_review_someone_elses_purchase()
    {
        var (item, order) = DeliveredOrder();

        var result = ReviewEligibility.Evaluate(item, order, Guid.NewGuid(), false, Now);

        result.CanReview.Should().BeFalse();
        result.Reason.Should().Contain("customer who placed the order");
    }

    [Fact]
    public void An_undelivered_order_cannot_be_reviewed()
    {
        // A not-yet-delivered order proves the rule blocks the review.
        var (pendingItem, pendingOrder) = PendingOrder();

        var result = ReviewEligibility.Evaluate(pendingItem, pendingOrder, CustomerId, false, Now);

        result.CanReview.Should().BeFalse();
        result.Reason.Should().Contain("delivered");
    }

    [Fact]
    public void An_item_can_only_be_reviewed_once()
    {
        var (item, order) = DeliveredOrder();

        var result = ReviewEligibility.Evaluate(item, order, CustomerId, true, Now);

        result.CanReview.Should().BeFalse();
        result.Reason.Should().Contain("already been reviewed");
    }

    [Fact]
    public void A_missing_order_item_is_refused()
    {
        var result = ReviewEligibility.Evaluate(null, null, CustomerId, false, Now);

        result.CanReview.Should().BeFalse();
    }

    [Fact]
    public void The_review_window_closes_thirty_days_after_delivery()
    {
        var (item, order) = DeliveredOrder();

        ReviewEligibility.Evaluate(item, order, CustomerId, false, Now.AddDays(29)).CanReview.Should().BeTrue();

        var late = ReviewEligibility.Evaluate(item, order, CustomerId, false, Now.AddDays(31));
        late.CanReview.Should().BeFalse();
        late.Reason.Should().Contain("30-day review window");
    }

    private static (OrderItem Item, Order Order) PendingOrder()
    {
        var address = new OrderAddressSnapshot("Home", "Aarav", "+9779800000000", "Street", null, "Kathmandu", null, "44600", "NP");
        var order = Order.Place(CustomerId, address, null, null, 200m, 0m, 0m, 0m, "USD", "Mock", null, Now);

        var sellerOrder = SellerOrder.Create(order.Id, Guid.NewGuid(), "MP-2-01", 200m, 0m, 0m, 0m, 10m, Now);
        order.AddSellerOrder(sellerOrder);

        var item = OrderItem.Create(order.Id, sellerOrder.Id, Guid.NewGuid(), Guid.NewGuid(), sellerOrder.SellerId,
            Guid.NewGuid(), "Product", null, "Default", "SKU", 1, 200m, 200m, Now);

        order.AddItem(item);
        sellerOrder.AddItem(item);

        return (item, order);
    }

    private static (OrderItem Item, Order Order) DeliveredOrder()
    {
        var address = new OrderAddressSnapshot("Home", "Aarav", "+9779800000000", "Street", null, "Kathmandu", null, "44600", "NP");
        var order = Order.Place(CustomerId, address, null, null, 200m, 0m, 0m, 0m, "USD", "Mock", null, Now.AddDays(-5));

        var sellerOrder = SellerOrder.Create(order.Id, Guid.NewGuid(), "MP-1-01", 200m, 0m, 0m, 0m, 10m, Now.AddDays(-5));
        order.AddSellerOrder(sellerOrder);

        var item = OrderItem.Create(order.Id, sellerOrder.Id, Guid.NewGuid(), Guid.NewGuid(), sellerOrder.SellerId,
            Guid.NewGuid(), "Product", null, "Default", "SKU", 1, 200m, 200m, Now.AddDays(-5));

        order.AddItem(item);
        sellerOrder.AddItem(item);

        order.MarkPaid(Now.AddDays(-5));
        order.ChangeStatus(OrderStatus.Confirmed, null, null, Now.AddDays(-4));
        order.ChangeStatus(OrderStatus.Processing, null, null, Now.AddDays(-4));
        order.ChangeStatus(OrderStatus.Packed, null, null, Now.AddDays(-3));
        order.ChangeStatus(OrderStatus.Shipped, null, null, Now.AddDays(-2));
        order.ChangeStatus(OrderStatus.Delivered, null, null, Now.AddDays(-1));

        return (item, order);
    }
}
