using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Marketplace.IntegrationTests.Infrastructure;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Marketplace.IntegrationTests;

/// <summary>
/// Rotation is a data-level contract, not just an HTTP status: the presented token must end
/// up revoked, its successor must exist, and only the hash of a token may ever be stored.
/// </summary>
public sealed class RefreshTokenRotationTests : IClassFixture<MarketplaceApiFactory>, IAsyncLifetime
{
    private readonly MarketplaceApiFactory _factory;
    private MarketplaceTestData _data = null!;

    public RefreshTokenRotationTests(MarketplaceApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _data = await _factory.CreateDatabaseAsync();
        await _data.InitialiseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Rotation_revokes_the_presented_token_and_stores_only_hashes()
    {
        var (_, tokens) = await AuthHelper.RegisterAsync(_factory, "rotation-storage@test.dev");
        var original = tokens.RefreshToken;

        var http = new ApiClient(_factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        }));

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add("Cookie", $"mp_refresh={original}");

        (await http.Http.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var tokenRows = context.Set<Marketplace.Domain.Identity.RefreshToken>();

        var presented = await tokenRows.AsNoTracking().SingleAsync(t => t.TokenHash == Hash(original));
        var family = await tokenRows.AsNoTracking().Where(t => t.FamilyId == presented.FamilyId).ToListAsync();
        var successor = family.Single(t => t.Id == presented.RevokedByTokenId);

        presented.IsRevoked.Should().BeTrue();
        presented.RevokedReason.Should().Be("rotated");
        successor.IsRevoked.Should().BeFalse();
        successor.UserId.Should().Be(presented.UserId);

        // The raw token must never be recoverable from the database.
        family.Should().NotContain(t => t.TokenHash.Contains(original, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reuse_revokes_every_token_in_the_family()
    {
        var (_, tokens) = await AuthHelper.RegisterAsync(_factory, "reuse-family@test.dev");

        var http = new ApiClient(_factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        }));

        var first = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh") { Content = JsonContent.Create(new { }) };
        first.Headers.Add("Cookie", $"mp_refresh={tokens.RefreshToken}");
        (await http.Http.SendAsync(first)).StatusCode.Should().Be(HttpStatusCode.OK);

        var replay = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh") { Content = JsonContent.Create(new { }) };
        replay.Headers.Add("Cookie", $"mp_refresh={tokens.RefreshToken}");
        (await http.Http.SendAsync(replay)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();

        var registered = await context.Users.AsNoTracking().FirstAsync(u => u.Email == "reuse-family@test.dev");
        var family = await context.Set<Marketplace.Domain.Identity.RefreshToken>()
            .AsNoTracking()
            .Where(t => t.UserId == registered.Id)
            .ToListAsync();

        family.Should().NotBeEmpty();
        family.Should().OnlyContain(t => t.IsRevoked, "a detected replay must kill the whole family");
    }

    /// <summary>Mirrors the server-side hash so the test can find the row it just rotated.</summary>
    private static string Hash(string rawToken)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes);
    }
}
