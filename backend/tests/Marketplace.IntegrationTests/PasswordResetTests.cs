using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Modules.Auth.DTOs;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Marketplace.IntegrationTests;

/// <summary>
/// The password reset flow, end to end.
///
/// The properties that matter are not the happy path: that an unknown address is answered
/// exactly like a known one, that a link works once, and that using it ends every session the
/// account had. Each of those is a way an account is taken over, so each is asserted directly.
///
/// Every case that changes a password does it on an account it registered itself. The seeded
/// customer is shared by the rest of the class, and a suite that quietly invalidates its own
/// fixture is a suite whose failures depend on ordering.
/// </summary>
public sealed partial class PasswordResetTests : IClassFixture<MarketplaceApiFactory>, IAsyncLifetime
{
    private readonly MarketplaceApiFactory _factory;
    private MarketplaceTestData _data = null!;
    private ApiClient _anonymous = null!;

    public PasswordResetTests(MarketplaceApiFactory factory) => _factory = factory;

    private RecordingEmailSender Inbox => (RecordingEmailSender)_factory.Services.GetRequiredService<IEmailSender>();

    public async Task InitializeAsync()
    {
        _data = await _factory.CreateDatabaseAsync();
        await _data.InitialiseAsync();

        _anonymous = new ApiClient(_factory.CreateClient());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_reset_link_changes_the_password_and_lets_the_new_one_in()
    {
        var account = await NewAccountAsync();

        var token = await RequestResetTokenAsync(account.Email);
        token.Should().NotBeNullOrEmpty("a link that never arrives is worth nothing to anyone locked out");

        var reset = await ResetAsync(token!, "NewPassword9");
        reset.StatusCode.Should().Be(HttpStatusCode.NoContent, await ApiClient.ReadTextAsync(reset));

        // The old password has to stop working, or the reset was theatre.
        var oldAttempt = await _anonymous.PostAsync("/api/auth/login", new LoginRequest(account.Email, MarketplaceTestData.CustomerPassword));
        oldAttempt.StatusCode.Should().NotBe(HttpStatusCode.OK);

        var newAttempt = await _anonymous.PostAsync("/api/auth/login", new LoginRequest(account.Email, "NewPassword9"));
        newAttempt.StatusCode.Should().Be(HttpStatusCode.OK, await ApiClient.ReadTextAsync(newAttempt));
    }

    [Fact]
    public async Task A_reset_link_works_exactly_once()
    {
        var account = await NewAccountAsync();
        var token = await RequestResetTokenAsync(account.Email);

        (await ResetAsync(token!, "AnotherPass7")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await ResetAsync(token!, "ThirdPass7");
        second.StatusCode.Should().Be(HttpStatusCode.NotFound, "a spent link is not a link any more");

        var login = await _anonymous.PostAsync("/api/auth/login", new LoginRequest(account.Email, "AnotherPass7"));
        login.StatusCode.Should().Be(HttpStatusCode.OK, "the first password that was actually set still stands");
    }

    [Fact]
    public async Task A_second_request_retires_the_first_link()
    {
        var account = await NewAccountAsync();

        var first = await RequestResetTokenAsync(account.Email);
        var second = await RequestResetTokenAsync(account.Email);

        second.Should().NotBe(first, "a second request issues a new link rather than resending the old one");

        (await ResetAsync(first!, "StalePass7")).StatusCode.Should().Be(HttpStatusCode.NotFound,
            "only the newest link works, so two cannot be used side by side");

        (await ResetAsync(second!, "FreshPass7")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Resetting_ends_every_session_the_account_had()
    {
        var account = await NewAccountAsync();
        var (_, session) = await AuthHelper.SignInAsync(_factory, account.Email, MarketplaceTestData.CustomerPassword);

        var token = await RequestResetTokenAsync(account.Email);
        (await ResetAsync(token!, "SessionPass7")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Whoever asked for the reset may not be whoever was signed in, so a cookie issued before
        // it has to stop working rather than quietly continuing.
        var client = new ApiClient(_factory.CreateClient());
        var attempt = await client.PostAsync("/api/auth/refresh", new RefreshTokenRequest(session.RefreshToken));

        attempt.StatusCode.Should().NotBe(HttpStatusCode.OK, "a session that predates a reset must not survive it");
    }

    [Fact]
    public async Task An_unknown_address_is_answered_exactly_like_a_known_one()
    {
        var known = await _anonymous.PostAsync("/api/auth/forgot-password", new ForgotPasswordRequest(MarketplaceTestData.CustomerEmail));
        var unknown = await _anonymous.PostAsync("/api/auth/forgot-password", new ForgotPasswordRequest("nobody@test.dev"));

        known.StatusCode.Should().Be(HttpStatusCode.NoContent);
        unknown.StatusCode.Should().Be(HttpStatusCode.NoContent, "a different answer is a way to find out who has an account here");
        (await ApiClient.ReadTextAsync(unknown)).Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_address_receives_no_email()
    {
        await _anonymous.PostAsync("/api/auth/forgot-password", new ForgotPasswordRequest("nobody-at-all@test.dev"));

        Inbox.For("nobody-at-all@test.dev").Should().BeNull();
    }

    [Fact]
    public async Task A_guessed_token_is_refused_and_changes_nothing()
    {
        var reset = await ResetAsync("not-a-real-token", "GuessedPass7");

        reset.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // A refused reset must not lock anyone out of the password they already have.
        var login = await _anonymous.PostAsync("/api/auth/login", new LoginRequest(MarketplaceTestData.CustomerEmail, MarketplaceTestData.CustomerPassword));
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_reset_is_audited()
    {
        await RequestResetAsync(MarketplaceTestData.CustomerEmail);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();

        var requested = await context.AuditLogs.AsNoTracking()
            .CountAsync(a => a.Action == AuditAction.PasswordResetRequested);

        requested.Should().BeGreaterThan(0, "an account takeover has to leave a trail");
    }

    [Fact]
    public async Task Only_a_hash_of_the_token_is_stored()
    {
        var token = await RequestResetTokenAsync(MarketplaceTestData.CustomerEmail);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();

        var stored = await context.PasswordResetTokens.AsNoTracking()
            .Where(t => t.UserId == _data.CustomerId)
            .ToListAsync();

        stored.Should().NotBeEmpty();
        stored.Select(t => t.TokenHash).Should().NotContain(token,
            "a database readable by someone who is not the user must not hold a usable reset link");
    }

    [Fact]
    public async Task The_link_points_at_the_configured_frontend()
    {
        var token = await RequestResetTokenAsync(MarketplaceTestData.CustomerEmail);
        var message = Inbox.For(MarketplaceTestData.CustomerEmail);

        message.Should().NotBeNull();
        message!.TextBody.Should().Contain("/reset-password?token=", "the link has to land on a page that exists");
        message.TextBody.Should().Contain(token);
    }

    private Task<HttpResponseMessage> RequestResetAsync(string email) =>
        _anonymous.PostAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));

    /// <summary>Requests a reset and reads the token out of the link that was mailed.</summary>
    private async Task<string?> RequestResetTokenAsync(string email)
    {
        var response = await RequestResetAsync(email);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await ApiClient.ReadTextAsync(response));

        var message = Inbox.For(email);
        message.Should().NotBeNull($"a request for {email} should have produced a message");

        return TokenFrom(message!.TextBody ?? message.HtmlBody ?? string.Empty);
    }

    private async Task<HttpResponseMessage> ResetAsync(string token, string password) =>
        await _anonymous.PostAsync("/api/auth/reset-password", new ResetPasswordRequest(token, password, password));

    private async Task<(string Email, ApiClient Client)> NewAccountAsync()
    {
        var email = $"reset-{Guid.NewGuid():N}@test.dev";
        var (client, _) = await AuthHelper.RegisterAsync(_factory, email, MarketplaceTestData.CustomerPassword);

        return (email, client);
    }

    /// <summary>Pulls the token out of the link the way a user would copy it.</summary>
    private static string? TokenFrom(string body) => TokenPattern().Match(body) is { Success: true } match ? match.Groups[1].Value : null;

    [GeneratedRegex(@"token=([A-Za-z0-9\-_]+)")]
    private static partial Regex TokenPattern();
}
