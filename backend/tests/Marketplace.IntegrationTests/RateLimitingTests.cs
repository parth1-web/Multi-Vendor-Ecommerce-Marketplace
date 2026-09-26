using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Marketplace.Application.Modules.Auth.DTOs;
using Marketplace.IntegrationTests.Infrastructure;
using Xunit;

namespace Marketplace.IntegrationTests;

/// <summary>
/// The suite raises the rate limits so its own traffic is not throttled. This class turns one
/// limiter back down to a tiny value to prove the limiter is actually wired to configuration
/// and rejects excess callers with 429 rather than letting them through.
/// </summary>
public sealed class RateLimitingTests
{
    [Fact]
    public async Task Auth_endpoints_reject_a_caller_that_exceeds_the_configured_limit()
    {
        var factory = new MarketplaceApiFactory()
            .WithSettings(("RateLimiting:AuthPermitLimit", "3"), ("RateLimiting:GlobalPermitLimit", "100000"));

        try
        {
            var data = await factory.CreateDatabaseAsync();
            await data.InitialiseAsync();

            var statuses = new List<HttpStatusCode>();
            var client = new ApiClient(factory.CreateClient());

            for (var attempt = 0; attempt < 6; attempt++)
            {
                var response = await client.PostAsync(
                    "/api/auth/login",
                    new LoginRequest(MarketplaceTestData.CustomerEmail, "WrongPassword@1"));
                statuses.Add(response.StatusCode);
            }

            statuses.Should().Contain(HttpStatusCode.TooManyRequests, "the auth limiter must reject the caller");
            statuses.TakeWhile(status => status != HttpStatusCode.TooManyRequests)
                .Should().AllSatisfy(status => status.Should().Be(HttpStatusCode.UnprocessableEntity));
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Rate_limiting_is_not_applied_when_the_caller_is_under_the_limit()
    {
        var factory = new MarketplaceApiFactory().WithSettings(("RateLimiting:AuthPermitLimit", "50"));

        try
        {
            var data = await factory.CreateDatabaseAsync();
            await data.InitialiseAsync();

            var client = new ApiClient(factory.CreateClient());
            var response = await client.PostAsync(
                "/api/auth/login",
                new LoginRequest(MarketplaceTestData.CustomerEmail, MarketplaceTestData.CustomerPassword));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }
}
