using System.Net;
using System.Text;
using FluentAssertions;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Catalog.DTOs;
using Marketplace.Application.Modules.Orders.DTOs;
using Marketplace.Application.Modules.Notifications.DTOs;
using Marketplace.Application.Modules.Reviews.DTOs;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Orders;
using Marketplace.IntegrationTests.Infrastructure;
using Xunit;

namespace Marketplace.IntegrationTests;

/// <summary>
/// Reviews.
///
/// A review is only worth reading if it was written by somebody who bought the thing, so every
/// rule here is about who may write one and when. It is also the part of the marketplace most
/// easily broken without anybody noticing: the product page shows reviews, so an empty list looks
/// like a quiet product rather than a broken feature.
/// </summary>
public sealed class ReviewTests : IClassFixture<MarketplaceApiFactory>, IAsyncLifetime
{
    private readonly MarketplaceApiFactory _factory;
    private MarketplaceTestData _data = null!;

    public ReviewTests(MarketplaceApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _data = await _factory.CreateDatabaseAsync();
        await _data.InitialiseAsync();
    }


    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_customer_who_received_the_product_can_review_it()
    {
        var (customer, order, item) = await DeliveredOrderAsync();
        var before = await PublicReviewCountAsync(item.ProductId);

        var created = await customer.PostAsync($"/api/products/{item.ProductId}/reviews", new
        {
            rating = 5,
            title = "Does what it says",
            body = "Posted against a delivered order, so this is a real review of a real purchase.",
            orderItemId = item.Id,
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created, await ApiClient.ReadTextAsync(created));
        var review = await ApiClient.ReadAsync<ReviewResponse>(created);

        review!.Rating.Should().Be(5);
        review.IsVerifiedPurchase.Should().BeTrue("a review written against a delivered line is a purchase");
        review.AuthorName.Should().NotBeNullOrWhiteSpace("a review says who wrote it");

        (await PublicReviewCountAsync(item.ProductId)).Should().Be(before + 1);
    }

    [Fact]
    public async Task The_seller_is_told_about_a_review()
    {
        // The notification is addressed to a user, and the sellers and users are different rows
        // with different ids. Handing the seller id to the notifications table fails on its foreign
        // key, and because that happens after the review is written it takes the whole request
        // down with a 500 rather than quietly skipping the notification.
        var (customer, _, item) = await DeliveredOrderAsync();

        var created = await customer.PostAsync($"/api/products/{item.ProductId}/reviews", new
        {
            rating = 4,
            title = "Good, with one caveat",
            body = "Noting the caveat so the seller can act on it, and so this review is worth reading.",
            orderItemId = item.Id,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await ApiClient.ReadTextAsync(created));

        var (seller, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);
        var notifications = await seller.GetAsync<PagedResult<NotificationResponse>>("/api/notifications?page=1&pageSize=50");

        notifications.Should().NotBeNull();
        notifications!.Items.Should().Contain(n => n.Type == NotificationType.NewReview, "the seller is told a review arrived");

    }

    [Fact]
    public async Task A_review_of_a_product_that_has_not_arrived_is_refused()
    {
        var (customer, order, _) = await PendingOrderAsync();
        var item = order.Items[0];

        var refused = await customer.PostAsync($"/api/products/{item.ProductId}/reviews", new
        {
            rating = 5,
            title = "Too early",
            body = "This has not arrived yet, so this review should not be accepted.",
            orderItemId = item.Id,
        });

        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await ApiClient.ReadTextAsync(refused));
    }

    [Fact]
    public async Task A_second_review_of_the_same_line_is_refused()
    {
        var (customer, _, item) = await DeliveredOrderAsync();
        var body = new
        {
            rating = 4,
            title = "Once is enough",
            body = "The second attempt at reviewing the same line of the same order should be refused.",
            orderItemId = item.Id,
        };

        (await customer.PostAsync($"/api/products/{item.ProductId}/reviews", body)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await customer.PostAsync($"/api/products/{item.ProductId}/reviews", body)).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "one purchase is one review");
    }

    [Fact]
    public async Task A_line_from_someone_elses_order_is_refused()
    {
        var (_, _, item) = await DeliveredOrderAsync();
        var (other, _, _) = await DeliveredOrderAsync();

        var refused = await other.PostAsync($"/api/products/{item.ProductId}/reviews", new
        {
            rating = 1,
            title = "Not mine to review",
            body = "This line belongs to a different customer's order, so it is not theirs to review.",
            orderItemId = item.Id,
        });

        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await ApiClient.ReadTextAsync(refused));
    }

    [Fact]
    public async Task An_anonymous_visitor_cannot_write_a_review()
    {
        var (_, _, item) = await DeliveredOrderAsync();
        var anonymous = new ApiClient(_factory.CreateClient());

        var refused = await anonymous.PostAsync($"/api/products/{item.ProductId}/reviews", new
        {
            rating = 5,
            title = "No account",
            body = "Writing a review needs an account, because the purchase has to be provable.",
            orderItemId = item.Id,
        });

        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_sellers_reply_reaches_the_product_page()
    {
        // The review is written, the reply is written, the seller sees the reply on their own
        // screen — and the shopper, who is the person it was written for, saw nothing. The
        // product's reviews were loaded without their replies, so the field was always null on
        // the way out. It passes through a cache, so a stale copy is the other half of the risk.
        var (customer, _, item) = await DeliveredOrderAsync();
        var created = await customer.PostAsync($"/api/products/{item.ProductId}/reviews", new
        {
            rating = 3,
            title = "Does not go back in the box",
            body = "The cable was loose in the packaging, so this is a note for whoever reads it.",
            orderItemId = item.Id,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var review = (await ApiClient.ReadAsync<ReviewResponse>(created))!;
        var (seller, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);
        var replied = await seller.PostAsync($"/api/reviews/{review.Id}/reply",
            new { body = "Thank you. We have changed the packaging and it is now taped in place." });
        replied.StatusCode.Should().Be(HttpStatusCode.Created, await ApiClient.ReadTextAsync(replied));

        var anonymous = new ApiClient(_factory.CreateClient());
        var product = await anonymous.GetAsync<ProductDetailResponse>($"/api/products/{item.ProductId}");

        var shown = product!.Reviews.Single(r => r.Id == review.Id);
        shown.Reply.Should().NotBeNull("a reply the seller wrote is the point of writing it");
        shown.Reply!.Body.Should().Contain("taped in place");
        shown.Reply.SellerName.Should().NotBeNullOrWhiteSpace("and it is labelled as the store's, not as another customer");

        // And the seller can see what they wrote, which is the other half. The list and the
        // product page get their data by different routes: the page from the review's own query,
        // the list from a projection that cannot carry a navigation, so the reply has to be
        // fetched for the page separately. Getting one without the other is easy and invisible.
        var theirs = await seller.GetAsync<PagedResult<ReviewResponse>>("/api/reviews/seller?page=1&pageSize=50");
        var mine = theirs!.Items.SingleOrDefault(r => r.Id == review.Id);
        mine.Should().NotBeNull();
        mine!.Reply.Should().NotBeNull("a seller who has just replied can go back and see it");
        mine.Reply!.Body.Should().Contain("taped in place");
        mine.Reply.SellerName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_seller_may_only_reply_once()
    {
        var (customer, _, item) = await DeliveredOrderAsync();
        var created = await customer.PostAsync($"/api/products/{item.ProductId}/reviews", new
        {
            rating = 4,
            title = "Worth a reply",
            body = "Posting this so the one-reply rule is the thing being tested.",
            orderItemId = item.Id,
        });
        var review = (await ApiClient.ReadAsync<ReviewResponse>(created))!;

        var (seller, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);
        (await seller.PostAsync($"/api/reviews/{review.Id}/reply", new { body = "First reply, and the only one." }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await seller.PostAsync($"/api/reviews/{review.Id}/reply", new { body = "Actually, one more thing." });
        second.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await ApiClient.ReadTextAsync(second));
    }

    [Fact]
    public async Task A_seller_cannot_reply_to_a_review_of_someone_elses_product()
    {
        var (customer, _, item) = await DeliveredOrderAsync();
        var created = await customer.PostAsync($"/api/products/{item.ProductId}/reviews", new
        {
            rating = 2,
            title = "Not the seller's to answer",
            body = "This is on the other seller's product, so the reply must be refused.",
            orderItemId = item.Id,
        });
        var review = (await ApiClient.ReadAsync<ReviewResponse>(created))!;

        var (other, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SecondSellerEmail, MarketplaceTestData.SellerPassword);
        var refused = await other.PostAsync($"/api/reviews/{review.Id}/reply", new { body = "This is not my product." });

        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await ApiClient.ReadTextAsync(refused));
    }

    [Fact]
    public async Task A_rating_below_one_star_is_refused()

    {
        var (customer, _, item) = await DeliveredOrderAsync();

        var refused = await customer.PostAsync($"/api/products/{item.ProductId}/reviews", new
        {
            rating = 0,
            title = "No stars",
            body = "A rating of zero is not a rating, and would drag the average down for everyone.",
            orderItemId = item.Id,
        });

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest, await ApiClient.ReadTextAsync(refused));
    }

    [Fact]
    public async Task A_review_moves_the_products_average()
    {
        var (customer, _, item) = await DeliveredOrderAsync();
        var before = await RatingAsync(item.ProductId);

        var created = await customer.PostAsync($"/api/products/{item.ProductId}/reviews", new
        {
            rating = 5,
            title = "Worth five",
            body = "So that the average on the product page is known to have moved by the end of this test.",
            orderItemId = item.Id,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var after = await RatingAsync(item.ProductId);
        after.Total.Should().Be(before.Total + 1);
        after.Average.Should().NotBe(before.Average, "the product page shows the average, so it has to be kept");
    }

    [Fact]
    public async Task A_seller_sees_the_reviews_of_their_own_products_only()
    {
        var (customer, _, item) = await DeliveredOrderAsync();
        await customer.PostAsync($"/api/products/{item.ProductId}/reviews", new
        {
            rating = 4,
            title = "For the seller's list",
            body = "This should show in the seller's own review list, which includes hidden ones.",
            orderItemId = item.Id,
        });

        var (seller, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);
        var theirs = await seller.GetAsync<PagedResult<ReviewResponse>>("/api/reviews/seller?page=1&pageSize=50");
        theirs.Should().NotBeNull();
        theirs!.Items.Should().Contain(r => r.ProductId == item.ProductId);

        var (fashion, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SecondSellerEmail, MarketplaceTestData.SellerPassword);
        var notTheirs = await fashion.GetAsync<PagedResult<ReviewResponse>>("/api/reviews/seller?page=1&pageSize=50");
        notTheirs!.Items.Should().NotContain(r => r.ProductId == item.ProductId, "a seller reads their own products only");
    }

    private async Task<RatingBreakdownResponse> RatingAsync(Guid productId)
    {
        var client = new ApiClient(_factory.CreateClient());
        var product = await client.GetAsync<ProductDetailResponse>($"/api/products/{productId}");

        return product!.RatingBreakdown;
    }

    private async Task<int> PublicReviewCountAsync(Guid productId)
    {
        var client = new ApiClient(_factory.CreateClient());
        var page = await client.GetAsync<PagedResult<ReviewResponse>>($"/api/products/{productId}/reviews?page=1&pageSize=50");

        return page?.TotalCount ?? 0;
    }

    /// <summary>A fresh customer, and an order of theirs that has been delivered.</summary>
    private async Task<(ApiClient Customer, OrderResponse Order, OrderItemResponse Item)> DeliveredOrderAsync()
    {
        var (customer, order, item) = await PendingOrderAsync();
        await MoveToDeliveredAsync(order.Id);

        var refreshed = await customer.GetAsync<OrderResponse>($"/api/orders/{order.Id}");
        return (customer, refreshed!, item);
    }

    private async Task<(ApiClient Customer, OrderResponse Order, OrderItemResponse Item)> PendingOrderAsync()
    {
        var (customer, addressId) = await NewCustomerAsync();
        var added = await customer.PostAsync("/api/cart/items", new AddCartItemRequest(
            _data.SellerAProductId, _data.SellerAProductVariantId, 1));
        added.StatusCode.Should().Be(HttpStatusCode.OK, await ApiClient.ReadTextAsync(added));

        var placed = await customer.PostAsync("/api/checkout",
            new CheckoutRequest(addressId, "Mock", null, "Integration test order", null, Guid.NewGuid().ToString("N")));
        placed.StatusCode.Should().Be(HttpStatusCode.Created, await ApiClient.ReadTextAsync(placed));

        var order = (await ApiClient.ReadAsync<CheckoutResponse>(placed))!;
        var detail = await customer.GetAsync<OrderResponse>($"/api/orders/{order.OrderId}");

        return (customer, detail!, detail!.Items[0]);
    }

    /// <summary>Walks a seller order the whole way to delivered, as the seller would.</summary>
    private async Task MoveToDeliveredAsync(Guid orderId)
    {
        var (seller, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);
        var page = await seller.GetAsync<PagedResult<SellerOrderSummaryResponse>>("/api/seller/orders?page=1&pageSize=50");
        var sellerOrder = page!.Items.Single(o => o.OrderId == orderId);

        foreach (var status in new[] { SellerOrderStatus.Confirmed, SellerOrderStatus.Processing, SellerOrderStatus.Packed })
        {
            var step = await seller.PutAsync($"/api/seller/orders/{sellerOrder.Id}/status",
                new { status = status.ToString(), note = "on its way" });
            step.StatusCode.Should().Be(HttpStatusCode.NoContent, $"{status}: {await ApiClient.ReadTextAsync(step)}");
        }

        var shipped = await seller.PutAsync($"/api/seller/orders/{sellerOrder.Id}/status",
            new { status = SellerOrderStatus.Shipped.ToString(), note = "on its way", carrierName = "Nepal Post", trackingNumber = "NP123456789" });
        shipped.StatusCode.Should().Be(HttpStatusCode.NoContent, await ApiClient.ReadTextAsync(shipped));

        var delivered = await seller.PutAsync($"/api/seller/orders/{sellerOrder.Id}/status",
            new { status = SellerOrderStatus.Delivered.ToString(), note = "handed over" });
        delivered.StatusCode.Should().Be(HttpStatusCode.NoContent, await ApiClient.ReadTextAsync(delivered));
    }

    private async Task<(ApiClient Client, Guid AddressId)> NewCustomerAsync()
    {
        var email = $"reviewer-{Guid.NewGuid():N}@test.dev";
        var (client, _) = await AuthHelper.RegisterAsync(_factory, email, MarketplaceTestData.CustomerPassword);

        var created = await client.PostAsync("/api/addresses", new CreateAddressRequest(
            "Home", "Test Reviewer", "+9779800000000", "12 Ratna Marg", null, "Kathmandu", null, "44600", "NP", true));
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        return (client, (await ApiClient.ReadAsync<AddressResponse>(created))!.Id);
    }
}

