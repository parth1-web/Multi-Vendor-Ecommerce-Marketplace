using FluentAssertions;
using System.Net;
using System.Net.Http.Json;
using Marketplace.Application.Modules.Auth.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;
using Marketplace.IntegrationTests.Infrastructure;
using Marketplace.Domain.Enums;
using Xunit;

namespace Marketplace.IntegrationTests;

public sealed class AuthenticationTests : IClassFixture<MarketplaceApiFactory>, IAsyncLifetime
{
    private readonly MarketplaceApiFactory _factory;
    private MarketplaceTestData _data = null!;

    public AuthenticationTests(MarketplaceApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _data = await _factory.CreateDatabaseAsync();
        await _data.InitialiseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Register_creates_an_account_and_returns_a_token_pair()
    {
        var client = new ApiClient(_factory.CreateClient());
        var response = await client.PostAsync("/api/auth/register",
            new RegisterRequest("new-user@test.dev", "Customer@123", "Customer@123", "New", "User", null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ApiClient.ReadAsync<TokenResponse>(response);
        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.User.Email.Should().Be("new-user@test.dev");

        // The access token is the only credential in the body; the refresh token must stay
        // out of a payload that could be logged or cached by a script.
        body.RefreshToken.Should().BeNull();
        ApiClient.ReadRefreshCookie(response).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Register_issues_the_refresh_token_as_an_http_only_cookie()
    {
        var client = new ApiClient(_factory.CreateClient());
        var response = await client.PostAsync("/api/auth/register",
            new RegisterRequest("cookie-user@test.dev", "Customer@123", "Customer@123", "Cookie", "User", null));

        var cookie = response.Headers.GetValues("Set-Cookie").First();
        cookie.Should().Contain("mp_refresh=");
        cookie.Should().Contain("httponly");
        cookie.Should().Contain("samesite=lax");
    }

    [Fact]
    public async Task Register_rejects_a_duplicate_email_with_a_field_error()
    {
        var client = new ApiClient(_factory.CreateClient());
        var response = await client.PostAsync("/api/auth/register",
            new RegisterRequest(MarketplaceTestData.CustomerEmail, "Customer@123", "Customer@123", "Dup", "Licate", null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiClient.ReadTextAsync(response)).Should().Contain("already exists");
    }

    [Fact]
    public async Task Register_rejects_a_weak_password()
    {
        var client = new ApiClient(_factory.CreateClient());
        var response = await client.PostAsync("/api/auth/register",
            new RegisterRequest("weak@test.dev", "weak", "weak", "Weak", "Password", null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_refuses_to_self_register_as_admin()
    {
        var client = new ApiClient(_factory.CreateClient());
        var response = await client.PostAsync("/api/auth/register",
            new RegisterRequest("sneaky@test.dev", "Customer@123", "Customer@123", "Sneaky", "Admin", null, UserRole.Admin));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_succeeds_with_valid_credentials()
    {
        var client = new ApiClient(_factory.CreateClient());
        var response = await client.PostAsync("/api/auth/login",
            new LoginRequest(MarketplaceTestData.CustomerEmail, MarketplaceTestData.CustomerPassword));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ApiClient.ReadAsync<TokenResponse>(response))!.User.Role.Should().Be(UserRole.Customer);
    }

    [Fact]
    public async Task Login_rejects_a_wrong_password()
    {
        var client = new ApiClient(_factory.CreateClient());
        var response = await client.PostAsync("/api/auth/login",
            new LoginRequest(MarketplaceTestData.CustomerEmail, "WrongPassword@1"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Login_gives_the_same_answer_for_an_unknown_email()
    {
        var client = new ApiClient(_factory.CreateClient());

        var unknown = await client.PostAsync("/api/auth/login", new LoginRequest("nobody@test.dev", "Customer@123"));
        var wrongPassword = await client.PostAsync("/api/auth/login", new LoginRequest(MarketplaceTestData.CustomerEmail, "WrongPassword@1"));

        unknown.StatusCode.Should().Be(wrongPassword.StatusCode);
        (await ApiClient.ReadTextAsync(unknown)).Should().Be(await ApiClient.ReadTextAsync(wrongPassword));
    }

    [Fact]
    public async Task Me_returns_the_authenticated_identity()
    {
        var (client, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.CustomerEmail, MarketplaceTestData.CustomerPassword);

        var user = await client.GetAsync<UserResponse>("/api/auth/me");

        user.Should().NotBeNull();
        user!.Email.Should().Be(MarketplaceTestData.CustomerEmail);
    }

    [Fact]
    public async Task Me_requires_authentication()
    {
        var client = new ApiClient(_factory.CreateClient());

        var response = await client.Http.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_seller_token_carries_the_seller_scope()
    {
        var (client, _) = await AuthHelper.SignInAsync(_factory, MarketplaceTestData.SellerEmail, MarketplaceTestData.SellerPassword);

        var user = await client.GetAsync<UserResponse>("/api/auth/me");

        user!.SellerId.Should().NotBeNull();
        user.SellerId.Should().Be(_data.SellerAId);
        user.SellerStatus.Should().Be(nameof(SellerStatus.Active));
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_invalidates_the_previous_one()
    {
        var (_, tokens) = await AuthHelper.RegisterAsync(_factory, "rotate@test.dev");

        var client = new ApiClient(_factory.CreateClient());
        var first = await client.PostAsync("/api/auth/refresh", new { });

        first.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the refresh token lives in a cookie the raw client does not send");

        // A client that does not manage cookies, so the token under test is exactly the one
        // the test sends. Letting a cookie jar run would silently rotate the newest token and
        // the replay below would prove nothing.
        var http = new ApiClient(_factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }));
        var original = tokens.RefreshToken;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add("Cookie", $"mp_refresh={original}");

        var response = await http.Http.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rotatedCookie = ApiClient.ReadRefreshCookie(response);
        rotatedCookie.Should().NotBeNullOrWhiteSpace();
        rotatedCookie.Should().NotBe(original);
        (await ApiClient.ReadAsync<TokenResponse>(response))!.AccessToken.Should().NotBe(tokens.AccessToken);

        // Replaying the original token must be treated as theft and fail.
        var replay = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh") { Content = JsonContent.Create(new { }) };
        replay.Headers.Add("Cookie", $"mp_refresh={original}");

        var replayResponse = await http.Http.SendAsync(replay);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.Conflict, await ApiClient.ReadTextAsync(replayResponse));
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var (client, tokens) = await AuthHelper.RegisterAsync(_factory, "logout@test.dev");
        var http = _factory.CreateClient();

        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout") { Content = JsonContent.Create(new { }) };
        logout.Headers.Add("Cookie", $"mp_refresh={tokens.RefreshToken}");
        (await http.SendAsync(logout)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh") { Content = JsonContent.Create(new { }) };
        refresh.Headers.Add("Cookie", $"mp_refresh={tokens.RefreshToken}");

        var response = await http.SendAsync(refresh);
        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Changing_the_password_invalidates_every_existing_session()
    {
        var (client, tokens) = await AuthHelper.RegisterAsync(_factory, "changer@test.dev", "Customer@123");

        var change = await client.PostAsync("/api/auth/change-password",
            new ChangePasswordRequest("Customer@123", "NewPassword@1", "NewPassword@1"));
        change.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var http = _factory.CreateClient();
        var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh") { Content = JsonContent.Create(new { }) };
        refresh.Headers.Add("Cookie", $"mp_refresh={tokens.RefreshToken}");

        (await http.SendAsync(refresh)).StatusCode.Should().NotBe(HttpStatusCode.OK);
    }
}
