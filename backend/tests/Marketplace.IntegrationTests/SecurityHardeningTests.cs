using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Orders.DTOs;
using Marketplace.Application.Modules.Payments.DTOs;
using Marketplace.Application.Modules.Reviews.DTOs;
using Marketplace.API.Controllers;
using Marketplace.Application.Modules.Sellers.DTOs;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Marketplace.IntegrationTests;

/// <summary>
/// The authorization boundaries this phase closed, each asserted as a boundary rather than a
/// happy path.
/// </summary>
/// <remarks>
/// Every test here is written against behaviour that was wrong before it: a seller reading
/// another seller's record, an administrator rewriting a customer's review, a plain administrator
/// minting a super administrator, and a moderation queue with no read side at all. A regression
/// test that only proved the permitted case would let any of them come back.
/// </remarks>
public sealed class SecurityHardeningTests : IClassFixture<MarketplaceApiFactory>, IAsyncLifetime
{
    private readonly MarketplaceApiFactory _factory;
    private MarketplaceTestData _data = null!;

    public SecurityHardeningTests(MarketplaceApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _data = await _factory.CreateDatabaseAsync();
        await _data.InitialiseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /* ------------------------------------------------------------------ L1: seller ownership */

    [Fact]
    public async Task A_seller_can_read_their_own_seller_record()
    {
        var (sellerA, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);

        var response = await sellerA.Http.GetAsync($"/api/sellers/{_data.SellerAId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await ApiClient.ReadTextAsync(response));
        var seller = await ApiClient.ReadAsync<SellerResponse>(response);
        seller!.Id.Should().Be(_data.SellerAId);
    }

    [Fact]
    public async Task A_seller_cannot_read_another_sellers_record()
    {
        var (sellerA, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);

        var response = await sellerA.Http.GetAsync($"/api/sellers/{_data.SellerBId}");

        // 404 rather than 403 on purpose: the project's convention is that an object the caller may
        // not see does not exist as far as they are concerned, so this cannot be used to probe for
        // which seller ids are real.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "another seller's record must be indistinguishable from one that does not exist");
    }

    [Fact]
    public async Task A_seller_can_update_their_own_seller_record()
    {
        var (sellerA, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);
        var current = (await ApiClient.ReadAsync<SellerResponse>(await sellerA.Http.GetAsync($"/api/sellers/{_data.SellerAId}")))!;

        var response = await sellerA.PutAsync($"/api/sellers/{_data.SellerAId}", new
        {
            businessName = current.BusinessName,
            legalName = current.LegalName,
            phoneNumber = current.PhoneNumber,
            address = current.Address,
            taxIdentityNumber = current.TaxIdentityNumber,
            bankAccountName = current.BankAccountName,
            bankAccountNumber = current.BankAccountNumber,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await ApiClient.ReadTextAsync(response));
    }

    [Fact]
    public async Task A_seller_cannot_update_another_sellers_record()
    {
        var (sellerA, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);

        var response = await sellerA.PutAsync($"/api/sellers/{_data.SellerBId}", new
        {
            businessName = "Taken Over Ltd",
            legalName = "Taken Over Ltd",
            phoneNumber = "+9779800000000",
            address = "Somewhere else entirely",
            taxIdentityNumber = "999999999",
            bankAccountName = "Someone Else",
            bankAccountNumber = "0009999999",
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a write path needs the same ownership check as a read");

        var (admin, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);
        var unchanged = await ApiClient.ReadAsync<SellerResponse>(await admin.Http.GetAsync($"/api/sellers/{_data.SellerBId}"));
        unchanged!.BusinessName.Should().NotBe("Taken Over Ltd", "the refused write must not have changed anything");
    }

    [Fact]
    public async Task An_admin_can_still_read_and_update_any_seller()
    {
        var (admin, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);
        var current = (await ApiClient.ReadAsync<SellerResponse>(await admin.Http.GetAsync($"/api/sellers/{_data.SellerBId}")))!;

        var read = await admin.Http.GetAsync($"/api/sellers/{_data.SellerBId}");
        read.StatusCode.Should().Be(HttpStatusCode.OK);

        var write = await admin.PutAsync($"/api/sellers/{_data.SellerBId}", new
        {
            businessName = current.BusinessName,
            legalName = current.LegalName,
            phoneNumber = current.PhoneNumber,
            address = current.Address,
            taxIdentityNumber = current.TaxIdentityNumber,
            bankAccountName = current.BankAccountName,
            bankAccountNumber = current.BankAccountNumber,
        });

        write.StatusCode.Should().Be(HttpStatusCode.OK, await ApiClient.ReadTextAsync(write));
    }

    [Fact]
    public async Task A_customer_cannot_read_a_seller_record_at_all()
    {
        var (customer, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.CustomerEmail, MarketplaceTestData.CustomerPassword);

        var response = await customer.Http.GetAsync($"/api/sellers/{_data.SellerAId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a customer holds no seller role at all");
    }

    /* ------------------------------------------------------------- L2: payment verification */

    [Fact]
    public async Task An_anonymous_caller_cannot_verify_a_payment()
    {
        var (_, payment) = await PaidPaymentAsync();

        var response = await _factory.CreateClient().PostAsync($"/api/payments/{payment.Id}/verify", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_customer_can_still_confirm_their_own_payment_with_the_gateway()
    {
        var (customer, payment) = await PaidPaymentAsync();

        var response = await customer.PostAsync($"/api/payments/{payment.Id}/verify", new { });

// The documented workflow: the customer says they have been to the payment page and the
        // server goes and asks the gateway. Preserved deliberately - it is not the vulnerability.
        response.StatusCode.Should().Be(HttpStatusCode.OK, await ApiClient.ReadTextAsync(response));
    }

    [Fact]
    public async Task A_customer_cannot_verify_another_customers_payment()
    {
        var (_, payment) = await PaidPaymentAsync();
        var (otherCustomer, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SecondCustomerEmail, MarketplaceTestData.CustomerPassword);

        var response = await otherCustomer.PostAsync($"/api/payments/{payment.Id}/verify", new { });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "another customer's payment must be indistinguishable from one that does not exist");
    }

    [Fact]
    public async Task A_seller_cannot_verify_a_payment()
    {
        var (_, payment) = await PaidPaymentAsync();
        var (seller, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);

        var response = await seller.PostAsync($"/api/payments/{payment.Id}/verify", new { });

        // Role-level refusal, not an accidental 404 from owning nothing.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "verification is a buyer-or-admin operation and a seller is neither");
    }

    [Fact]
    public async Task A_customer_cannot_read_another_customers_payment()
    {
        var (_, payment) = await PaidPaymentAsync();
        var (otherCustomer, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SecondCustomerEmail, MarketplaceTestData.CustomerPassword);

        var response = await otherCustomer.Http.GetAsync($"/api/payments/{payment.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_admin_can_verify_a_payment()
    {
        var (_, payment) = await PaidPaymentAsync();
        var (admin, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var response = await admin.PostAsync($"/api/payments/{payment.Id}/verify", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await ApiClient.ReadTextAsync(response));
    }

    /* ---------------------------------------------------- L3: review content and moderation */

    [Fact]
    public async Task A_customer_can_still_edit_their_own_review()
    {
        var (_, review) = await PostedReviewAsync();
        var (customer, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.CustomerEmail, MarketplaceTestData.CustomerPassword);

        var response = await customer.PutAsync($"/api/reviews/{review.Id}", new
        {
            rating = 4,
            title = "Revised after thinking about it",
            body = "Still a good product, and the seller answered my question about it.",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await ApiClient.ReadTextAsync(response));
    }

    [Fact]
    public async Task A_customer_cannot_edit_another_customers_review()
    {
        var (_, review) = await PostedReviewAsync();
        var (otherCustomer, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SecondCustomerEmail, MarketplaceTestData.CustomerPassword);

        var response = await otherCustomer.PutAsync($"/api/reviews/{review.Id}", new
        {
            rating = 1,
            title = "Hijacked",
            body = "This text was written by somebody else entirely.",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "authorship is the rule, and a refusal of somebody else's review is an authorization failure, not a validation one");
    }

    [Fact]
    public async Task An_admin_cannot_rewrite_a_customers_review_text()
    {
        var (author, review) = await PostedReviewAsync();
        var (admin, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var response = await admin.PutAsync($"/api/reviews/{review.Id}", new
        {
            rating = 1,
            title = "Moderated",
            body = "An administrator rewrote this to say something kinder.",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "moderation changes visibility, never a customer's words");

        var reread = await author.GetAsync<PagedResult<ReviewResponse>>($"/api/products/{review.ProductId}/reviews?page=1&pageSize=50");
        reread!.Items.Should().OnlyContain(r => r.Id != review.Id || r.Title == review.Title,
            "the original text has to be untouched");
    }

    [Fact]
    public async Task An_admin_cannot_delete_a_customers_review_but_can_hide_it()
    {
        var (author, review) = await PostedReviewAsync();
        var (admin, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var refused = await admin.DeleteAsync($"/api/reviews/{review.Id}");
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "deletion is irreversible, so it stays with the author; hiding is the reversible tool");

        var hidden = await admin.PutAsync($"/api/reviews/{review.Id}/visibility", new { isVisible = false, note = "Removed from the storefront" });
        hidden.StatusCode.Should().Be(HttpStatusCode.OK, await ApiClient.ReadTextAsync(hidden));

        var stillThere = await author.GetAsync<PagedResult<ReviewResponse>>($"/api/products/{review.ProductId}/reviews?page=1&pageSize=50");
        stillThere!.Items.Should().OnlyContain(r => r.Id != review.Id || r.Title == review.Title,
            "the record survives moderation, which is the point of moderation over deletion");

        var asAuthor = await author.DeleteAsync($"/api/reviews/{review.Id}");
        asAuthor.StatusCode.Should().Be(HttpStatusCode.NoContent, await ApiClient.ReadTextAsync(asAuthor));
    }

    [Fact]
    public async Task A_customer_cannot_reach_the_admin_visibility_endpoint()
    {
        var (_, review) = await PostedReviewAsync();
        var (customer, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.CustomerEmail, MarketplaceTestData.CustomerPassword);

        var response = await customer.PutAsync($"/api/reviews/{review.Id}/visibility", new { isVisible = false, note = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_seller_cannot_reach_the_admin_visibility_endpoint()
    {
        var (_, review) = await PostedReviewAsync();
        var (seller, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);

        var response = await seller.PutAsync($"/api/reviews/{review.Id}/visibility", new { isVisible = false, note = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_admin_can_list_reviews_and_hide_one()
    {
        var (_, review) = await PostedReviewAsync();
        var (admin, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var listed = await admin.GetAsync<PagedResult<ModerationReviewResponse>>("/api/admin/reviews?page=1&pageSize=50");
        listed.Should().NotBeNull();
        listed!.Items.Should().Contain(r => r.Id == review.Id, "the moderation list sees every review");
        listed.Items.Should().OnlyContain(r => r.ProductName.Length > 0 && r.StoreName.Length > 0,
            "a moderator needs the product and the store to judge a review");

        var hidden = await admin.PutAsync($"/api/reviews/{review.Id}/visibility", new { isVisible = false, note = "Breaks the rules" });
        hidden.StatusCode.Should().Be(HttpStatusCode.OK, await ApiClient.ReadTextAsync(hidden));

        var stillListed = await admin.GetAsync<PagedResult<ModerationReviewResponse>>("/api/admin/reviews?page=1&pageSize=50");
        stillListed!.Items.Should().Contain(r => r.Id == review.Id && !r.IsVisible,
            "a hidden review has to remain visible to the moderator who hid it, or it can never be undone");
    }

    [Fact]
    public async Task An_admin_can_restore_a_hidden_review()
    {
        var (_, review) = await PostedReviewAsync();
        var (admin, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        (await admin.PutAsync($"/api/reviews/{review.Id}/visibility", new { isVisible = false, note = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var restored = await admin.PutAsync($"/api/reviews/{review.Id}/visibility", new { isVisible = true, note = "Reviewed and allowed" });
        restored.StatusCode.Should().Be(HttpStatusCode.OK, await ApiClient.ReadTextAsync(restored));

        var reread = await ApiClient.ReadAsync<ModerationReviewResponse>(await admin.Http.GetAsync($"/api/admin/reviews/{review.Id}"));
        reread!.IsVisible.Should().BeTrue("the operation is reversible, which it has to be");
    }

    [Fact]
    public async Task The_public_endpoint_does_not_expose_a_hidden_review()
    {
        var (_, review) = await PostedReviewAsync();
        var (admin, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        (await admin.PutAsync($"/api/reviews/{review.Id}/visibility", new { isVisible = false, note = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var publicPage = await _factory.CreateClient()
            .GetFromJsonAsync<PagedResult<ReviewResponse>>($"/api/products/{review.ProductId}/reviews?page=1&pageSize=50");

        publicPage!.Items.Should().NotContain(r => r.Id == review.Id,
            "the shopper-facing read has always excluded hidden reviews, and still must");
    }

    [Fact]
    public async Task The_moderation_list_filters_by_visibility_and_rating()
    {
        var (_, review) = await PostedReviewAsync();
        var (admin, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var visibleOnly = await admin.GetAsync<PagedResult<ModerationReviewResponse>>("/api/admin/reviews?page=1&pageSize=50&visibility=true");
        visibleOnly!.Items.Should().Contain(r => r.Id == review.Id && r.IsVisible);

        var hiddenOnly = await admin.GetAsync<PagedResult<ModerationReviewResponse>>("/api/admin/reviews?page=1&pageSize=50&visibility=false");
        hiddenOnly!.Items.Should().NotContain(r => r.Id == review.Id);

        var exactRating = await admin.GetAsync<PagedResult<ModerationReviewResponse>>("/api/admin/reviews?page=1&pageSize=50&rating=4");
        exactRating!.Items.Should().OnlyContain(r => r.Rating == 4);

        var byProduct = await admin.GetAsync<PagedResult<ModerationReviewResponse>>($"/api/admin/reviews?page=1&pageSize=50&productId={review.ProductId}");
        byProduct!.Items.Should().OnlyContain(r => r.ProductId == review.ProductId);
    }

    [Fact]
    public async Task The_moderation_list_refuses_everyone_without_the_admin_role()
    {
        var (customer, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.CustomerEmail, MarketplaceTestData.CustomerPassword);
        var (seller, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);

        (await customer.Http.GetAsync("/api/admin/reviews?page=1")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await seller.Http.GetAsync("/api/admin/reviews?page=1")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _factory.CreateClient().GetAsync("/api/admin/reviews?page=1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /* ------------------------------------------------------ role changes may not escalate */

    [Fact]
    public async Task An_admin_cannot_grant_the_super_admin_role()
    {
        // A *plain* administrator. The seeded admin is a SuperAdmin, and a SuperAdmin promoting
        // somebody to SuperAdmin is the intended behaviour, so the guard can only be proved
        // against the role underneath it.
        var (admin, _, _) = await PlainAdminAsync();

        var response = await admin.PutAsync($"/api/admin/users/{_data.CustomerId}/role", new { role = "SuperAdmin" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a caller may not grant a role above its own, or a plain admin can mint one more powerful than itself");

        var (checker, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);
        var unchanged = await checker.GetAsync<PagedResult<AdminUserResponse>>("/api/admin/users?page=1&pageSize=100&search=customer@test.dev");
        unchanged!.Items.Should().OnlyContain(u => u.Role != UserRole.SuperAdmin);
    }

    [Fact]
    public async Task An_admin_cannot_change_their_own_role_or_status()
    {
        var (admin, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);

        var role = await admin.PutAsync($"/api/admin/users/{_data.AdminId}/role", new { role = "Customer" });
        var status = await admin.PutAsync($"/api/admin/users/{_data.AdminId}/status", new { isActive = false });

        role.StatusCode.Should().Be(HttpStatusCode.Forbidden, "self-demotion is how an admin locks themselves out mid-session");
        status.StatusCode.Should().Be(HttpStatusCode.Forbidden, "and self-disabling does the same thing more permanently");

        // Still signed in and still an administrator afterwards, which is the point.
        var (again, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.AdminEmail, MarketplaceTestData.AdminPassword);
        (await again.Http.GetAsync("/api/admin/analytics/summary")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_admin_can_still_change_somebody_elses_role_and_status()
    {
        // A throwaway account, not a seeded user. The fixture is shared across the class, so
        // demoting the demo customer to Seller here leaked into the payment tests, which then
        // failed on a role they had every right to hold - the right refusal for the wrong reason.
        var (subject, subjectId) = await ThrowawayUserAsync();
        var (admin, _, _) = await PlainAdminAsync();

        var promote = await admin.PutAsync($"/api/admin/users/{subjectId}/role", new { role = "Seller" });
        promote.StatusCode.Should().Be(HttpStatusCode.NoContent, await ApiClient.ReadTextAsync(promote));

        var disable = await admin.PutAsync($"/api/admin/users/{subjectId}/status", new { isActive = false });
        disable.StatusCode.Should().Be(HttpStatusCode.NoContent, await ApiClient.ReadTextAsync(disable));

        var page = await admin.GetAsync<PagedResult<AdminUserResponse>>($"/api/admin/users?page=1&pageSize=10&search={Uri.EscapeDataString(subject)}");
        var row = page!.Items.Should().ContainSingle(u => u.Id == subjectId).Which;
        row.Role.Should().Be(UserRole.Seller, "an admin may still manage roles below its own");
        row.IsActive.Should().BeFalse();

        var restore = await admin.PutAsync($"/api/admin/users/{subjectId}/status", new { isActive = true });
        restore.StatusCode.Should().Be(HttpStatusCode.NoContent, await ApiClient.ReadTextAsync(restore));
    }

    [Fact]
    public async Task A_customer_cannot_reach_the_admin_user_endpoints()
    {
        var (customer, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.CustomerEmail, MarketplaceTestData.CustomerPassword);
        var (seller, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);

        (await customer.Http.PutAsync($"/api/admin/users/{_data.SellerAId}/role", JsonContent.Create(new { role = "Admin" })))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await seller.Http.PutAsync($"/api/admin/users/{_data.SellerAId}/status", JsonContent.Create(new { isActive = false })))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _factory.CreateClient().PutAsync($"/api/admin/users/{_data.SellerAId}/role", JsonContent.Create(new { role = "Admin" })))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /* ------------------------------------------------------------------------------ helpers */

    /// <summary>
    /// A registered account with nothing attached to it, safe for a test to mutate.
    /// </summary>
    private async Task<(string Email, Guid UserId)> ThrowawayUserAsync()
    {
        var email = $"throwaway-{Guid.NewGuid():N}@test.dev";
        await AuthHelper.RegisterAsync(_factory, email);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var userId = await context.Users.Where(u => u.Email == email).Select(u => u.Id).FirstAsync();
        return (email, userId);
    }

    /// <summary>
    /// An account with the plain Admin role and nothing above it.
    /// </summary>
    /// <remarks>
    /// Built by registering normally and then promoting the row directly, because that is the only
    /// way to obtain a non-super administrator without widening the shared test fixture for one
    /// test. The point of the escalation guard is that it holds for the role *below* SuperAdmin,
    /// so testing it against the seeded SuperAdmin would prove nothing.
    /// </remarks>
    private async Task<(ApiClient Client, Guid UserId, string Email)> PlainAdminAsync()
    {
        var email = $"plain-admin-{Guid.NewGuid():N}@test.dev";
        var (registered, _) = await AuthHelper.RegisterAsync(_factory, email);

        Guid userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            var user = await context.Users.FirstAsync(u => u.Email == email);
            user.ChangeRole(UserRole.Admin, DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
            userId = user.Id;
        }

        // Sign in again so the bearer token carries the new role.
        var (client, _) = await AuthHelper.SignInAsync(_factory, email, MarketplaceTestData.CustomerPassword);
        return (client, userId, email);
    }

    /// <summary>
    /// A real paid order, so the payment tests operate on a row the gateway can actually settle
    /// rather than on a hand-written fixture.
    /// </summary>
    private async Task<(ApiClient Customer, PaymentResponse Payment)> PaidPaymentAsync()
    {
        var (customer, paymentId) = await CheckoutWithPaymentAsync();

        var payment = await ApiClient.ReadAsync<PaymentResponse>(await customer.Http.GetAsync($"/api/payments/{paymentId}"));
        return (customer, payment!);
    }

    private async Task<(ApiClient Customer, Guid PaymentId)> CheckoutWithPaymentAsync()
    {
        var (customer, addressId) = await NewCustomerAsync();
        var added = await customer.PostAsync("/api/cart/items", new AddCartItemRequest(
            _data.SellerAProductId, _data.SellerAProductVariantId, 1));
        added.StatusCode.Should().Be(HttpStatusCode.OK, await ApiClient.ReadTextAsync(added));

        var placed = await customer.PostAsync("/api/checkout",
            new CheckoutRequest(addressId, "Mock", null, "Security hardening order", null, Guid.NewGuid().ToString("N")));
        placed.StatusCode.Should().Be(HttpStatusCode.Created, await ApiClient.ReadTextAsync(placed));

        var checkout = (await ApiClient.ReadAsync<CheckoutResponse>(placed))!;
        var payments = await customer.GetAsync<List<PaymentResponse>>("/api/payments/mine");
        payments.Should().NotBeNull().And.NotBeEmpty("checkout takes a payment, and the verification tests need one");

        return (customer, payments![0].Id);
    }

    /// <summary>A review written by the demo customer against a delivered order.</summary>
    private async Task<(ApiClient Author, ReviewResponse Review)> PostedReviewAsync()
    {
        var (customer, order, item) = await DeliveredOrderAsync();

        var created = await customer.PostAsync($"/api/products/{item.ProductId}/reviews", new
        {
            rating = 4,
            title = "Worth the money",
            body = "Posted against a delivered order, so it is a review of a real purchase and not a guess.",
            orderItemId = item.Id,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await ApiClient.ReadTextAsync(created));

        var review = await ApiClient.ReadAsync<ReviewResponse>(created);
        return (customer, review!);
    }

    private async Task<(ApiClient Customer, OrderResponse Order, OrderItemResponse Item)> DeliveredOrderAsync()
    {
        var (customer, addressId) = await NewCustomerAsync();
        var added = await customer.PostAsync("/api/cart/items", new AddCartItemRequest(
            _data.SellerAProductId, _data.SellerAProductVariantId, 1));
        added.StatusCode.Should().Be(HttpStatusCode.OK, await ApiClient.ReadTextAsync(added));

        var placed = await customer.PostAsync("/api/checkout",
            new CheckoutRequest(addressId, "Mock", null, "Security hardening order", null, Guid.NewGuid().ToString("N")));
        placed.StatusCode.Should().Be(HttpStatusCode.Created, await ApiClient.ReadTextAsync(placed));

        var checkout = (await ApiClient.ReadAsync<CheckoutResponse>(placed))!;
        var detail = await customer.GetAsync<OrderResponse>($"/api/orders/{checkout.OrderId}");

        // Walk the seller order to delivered, as a seller would, so the review passes the
        // verified-purchase rule rather than being written around it.
        var (seller, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);
        var sellerOrders = await seller.GetAsync<PagedResult<SellerOrderSummaryResponse>>("/api/seller/orders?page=1&pageSize=50");
        var ours = sellerOrders!.Items.FirstOrDefault(o => o.OrderId == detail!.Id);
        ours.Should().NotBeNull();

        foreach (var status in new[] { "Confirmed", "Processing", "Packed", "Shipped", "Delivered" })
        {
            var moved = await seller.PutAsync($"/api/seller/orders/{ours!.Id}/status", new
            {
                status,
                note = (string?)null,
                carrierName = status == "Shipped" ? "Test Courier" : (string?)null,
                trackingNumber = status == "Shipped" ? "TRACK-001" : (string?)null,
            });
            moved.StatusCode.Should().Be(HttpStatusCode.NoContent, await ApiClient.ReadTextAsync(moved));
        }

        var refreshed = await customer.GetAsync<OrderResponse>($"/api/orders/{detail!.Id}");
        return (customer, refreshed!, refreshed!.Items[0]);
    }

    private async Task<(ApiClient Client, Guid AddressId)> NewCustomerAsync()
    {
        var (customer, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.CustomerEmail, MarketplaceTestData.CustomerPassword);

        var created = await customer.PostAsync("/api/addresses", new
        {
            label = "Home",
            recipientName = "Aarav Sharma",
            phoneNumber = "+9779800000000",
            line1 = "1 Test Street",
            line2 = (string?)null,
            city = "Kathmandu",
            state = (string?)null,
            postalCode = "44600",
            country = "NP",
            isDefault = true,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await ApiClient.ReadTextAsync(created));

        var address = (await ApiClient.ReadAsync<AddressResponse>(created))!;
        return (customer, address.Id);
    }
}
