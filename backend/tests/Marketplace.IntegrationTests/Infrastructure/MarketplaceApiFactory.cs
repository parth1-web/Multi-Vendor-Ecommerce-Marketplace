using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Modules.Auth.Abstractions;
using Marketplace.Application.Modules.Auth.DTOs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.Sqlite;

namespace Marketplace.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API pipeline against an in-memory relational database, a frozen clock,
/// an in-process cache and a no-op realtime notifier. No PostgreSQL or Redis required.
/// </summary>
public class MarketplaceApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Connection string for the shared in-memory SQLite database.</summary>
    public const string ConnectionName = "marketplace-tests";

    private bool _schemaCreated;

    /// <summary>Held open for the factory's lifetime so the shared in-memory database survives.</summary>
    private readonly SqliteConnection _keepAlive = new();

    private readonly string _connectionString = $"DataSource=marketplace-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

    /// <summary>Extra configuration values a test wants to override.</summary>
    private readonly Dictionary<string, string?> _overrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns a factory with the given configuration overrides applied.</summary>
    public MarketplaceApiFactory WithSettings(params (string Key, string? Value)[] settings)
    {
        foreach (var (key, value) in settings)
        {
            _overrides[key] = value;
        }

        return this;
    }

    /// <summary>Test time base so date-sensitive rules are deterministic.</summary>
    public DateTimeOffset Now { get; } = new(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);

    public FixedClock Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(config =>
        {
            var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["Jwt:Key"] = "integration-test-signing-key-0123456789-abcdefghij",
                ["Jwt:Issuer"] = "marketplace-api",
                ["Jwt:Audience"] = "marketplace-web",
                ["Jwt:AccessTokenMinutes"] = "30",
                ["Jwt:RefreshTokenDays"] = "30",
                ["Redis:Enabled"] = "false",
                ["Payment:DefaultProvider"] = "Mock",
                ["Payment:WebhookSecret"] = "integration-webhook-secret",
                ["Marketplace:CommissionRate"] = "10",
                ["Marketplace:FreeShippingThreshold"] = "150",
                ["Marketplace:StandardShippingCost"] = "9.99",
                ["Marketplace:ReservationMinutes"] = "15",
                ["Cors:AllowedOrigins:0"] = "http://localhost:3000",
                // The suite shares one caller identity, so production auth limits would throttle
                // the tests themselves. Rate limiting is verified separately with a low limit.
                ["RateLimiting:GlobalPermitLimit"] = "100000",
                ["RateLimiting:AuthPermitLimit"] = "100000",
                ["RateLimiting:CheckoutPermitLimit"] = "100000",
                ["RateLimiting:WebhookPermitLimit"] = "100000"
            };

            foreach (var (key, value) in _overrides)
            {
                settings[key] = value;
            }

            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            // Replace the Npgsql registration with SQLite.
            services.RemoveAll<DbContextOptions<Marketplace.Infrastructure.Persistence.MarketplaceDbContext>>();
            services.RemoveAll<DbContextOptions>();

            services.AddDbContext<Marketplace.Infrastructure.Persistence.MarketplaceDbContext>(options =>
            {
                options.UseSqlite(_connectionString);

                if (Environment.GetEnvironmentVariable("EF_SQL_LOG") == "1")
                {
                    var logPath = Path.Combine(Path.GetTempPath(), "ef-sql.log");
                    options.LogTo(message => File.AppendAllText(logPath, message + Environment.NewLine), LogLevel.Information)
                        .EnableSensitiveDataLogging();
                }
            });

            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);

            services.RemoveAll<IRealtimeNotifier>();
            services.AddSingleton<IRealtimeNotifier, RecordingRealtimeNotifier>();
        });
    }

    /// <summary>Creates the schema and seeds a deterministic marketplace.</summary>
    /// <summary>
    /// Creates the schema once per factory and returns a seeder bound to a scope that stays
    /// alive for the duration of the test class.
    /// </summary>
    public async Task<MarketplaceTestData> CreateDatabaseAsync()
    {
        if (_schemaCreated)
        {
            return new MarketplaceTestData(Services, Clock);
        }

        if (_keepAlive.State != System.Data.ConnectionState.Open)
        {
            _keepAlive.ConnectionString = _connectionString;
            await _keepAlive.OpenAsync();
        }

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Marketplace.Infrastructure.Persistence.MarketplaceDbContext>();

        await context.Database.EnsureCreatedAsync();

        _schemaCreated = true;
        return new MarketplaceTestData(Services, Clock);
    }
}

/// <summary>Deterministic clock so date-sensitive rules can be tested.</summary>
public sealed class FixedClock(DateTimeOffset? now = null) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now ?? new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

/// <summary>Captures real-time broadcasts so tests can assert on them.</summary>
public sealed class RecordingRealtimeNotifier : IRealtimeNotifier
{
    public List<string> Events { get; } = [];

    private void Record(string name)
    {
        lock (Events)
        {
            Events.Add(name);
        }
    }

    public Task OrderCreatedAsync(Guid orderId, string orderNumber, Guid customerId, IReadOnlyCollection<Guid> sellerIds, decimal totalAmount, CancellationToken cancellationToken = default)
    {
        Record(nameof(OrderCreatedAsync));
        return Task.CompletedTask;
    }

    public Task OrderUpdatedAsync(Guid orderId, string orderNumber, object payload, CancellationToken cancellationToken = default)
    {
        Record(nameof(OrderUpdatedAsync));
        return Task.CompletedTask;
    }

    public Task PaymentUpdatedAsync(Guid paymentId, Guid orderId, string status, CancellationToken cancellationToken = default)
    {
        Record(nameof(PaymentUpdatedAsync));
        return Task.CompletedTask;
    }

    public Task RefundUpdatedAsync(Guid refundId, Guid orderId, Guid customerId, string status, CancellationToken cancellationToken = default)
    {
        Record(nameof(RefundUpdatedAsync));
        return Task.CompletedTask;
    }

    public Task NotificationCreatedAsync(Guid userId, object notification, CancellationToken cancellationToken = default)
    {
        Record(nameof(NotificationCreatedAsync));
        return Task.CompletedTask;
    }

    public Task InventoryLowAsync(Guid sellerId, object payload, CancellationToken cancellationToken = default)
    {
        Record(nameof(InventoryLowAsync));
        return Task.CompletedTask;
    }

    public Task SellerStatusChangedAsync(Guid sellerId, string status, CancellationToken cancellationToken = default)
    {
        Record(nameof(SellerStatusChangedAsync));
        return Task.CompletedTask;
    }

    public Task AdminMetricsUpdatedAsync(object payload, CancellationToken cancellationToken = default)
    {
        Record(nameof(AdminMetricsUpdatedAsync));
        return Task.CompletedTask;
    }
}

/// <summary>HTTP helpers so each test reads as a scenario rather than plumbing.</summary>
public sealed class ApiClient(HttpClient client)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public HttpClient Http { get; } = client;

    public async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken = default) =>
        await Http.GetFromJsonAsync<T>(url, cancellationToken).ConfigureAwait(false);

    public async Task<HttpResponseMessage> PostAsync<T>(string url, T body, CancellationToken cancellationToken = default) =>
        await Http.PostAsJsonAsync(url, body, cancellationToken).ConfigureAwait(false);

    public async Task<HttpResponseMessage> PutAsync<T>(string url, T body, CancellationToken cancellationToken = default) =>
        await Http.PutAsJsonAsync(url, body, cancellationToken).ConfigureAwait(false);

    public async Task<HttpResponseMessage> PatchAsync<T>(string url, T body, CancellationToken cancellationToken = default) =>
        await Http.PatchAsJsonAsync(url, body, cancellationToken).ConfigureAwait(false);

    public async Task<HttpResponseMessage> DeleteAsync(string url, CancellationToken cancellationToken = default) =>
        await Http.DeleteAsync(url, cancellationToken).ConfigureAwait(false);

    public static async Task<T?> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(body) ? default : JsonSerializer.Deserialize<T>(body, Json);
    }

    public static async Task<string> ReadTextAsync(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync();

    /// <summary>
    /// Pulls the refresh token out of the HttpOnly cookie the API sets, which is the only
    /// place it travels. A test that needs to replay a refresh token has to read the cookie
    /// exactly as a browser would, never the response body.
    /// </summary>
    public static string? ReadRefreshCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            return null;
        }

        foreach (var cookie in setCookies)
        {
            var pair = cookie.Split(';', 2)[0];
            var separator = pair.IndexOf('=');
            if (separator < 0 || !pair.AsSpan(0, separator).SequenceEqual("mp_refresh"))
            {
                continue;
            }

            return WebUtility.UrlDecode(pair[(separator + 1)..]);
        }

        return null;
    }

    public void UseBearer(string accessToken) =>
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
}

/// <summary>Access token plus the refresh token the API placed in its HttpOnly cookie.</summary>
public sealed record SessionTokens(string AccessToken, string RefreshToken, TokenResponse Response);

/// <summary>Logs a user in and returns a client that carries the access token.</summary>
public static class AuthHelper
{
    public static async Task<(ApiClient Client, SessionTokens Tokens)> SignInAsync(
        MarketplaceApiFactory factory, string email, string password)
    {
        var client = new ApiClient(factory.CreateClient());
        var response = await client.PostAsync("/api/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();

        var body = await ApiClient.ReadAsync<TokenResponse>(response)
                   ?? throw new InvalidOperationException("Login did not return a token payload.");

        var refreshToken = ApiClient.ReadRefreshCookie(response)
                           ?? throw new InvalidOperationException("Login did not set a refresh cookie.");

        client.UseBearer(body.AccessToken);
        return (client, new SessionTokens(body.AccessToken, refreshToken, body));
    }

    public static async Task<(ApiClient Client, SessionTokens Tokens)> RegisterAsync(
        MarketplaceApiFactory factory,
        string email,
        string password = "Customer@123",
        string firstName = "Test",
        string lastName = "Customer")
    {
        var client = new ApiClient(factory.CreateClient());
        var response = await client.PostAsync("/api/auth/register", new RegisterRequest(email, password, password, firstName, lastName, null));
        response.EnsureSuccessStatusCode();

        var body = await ApiClient.ReadAsync<TokenResponse>(response)
                   ?? throw new InvalidOperationException("Registration did not return a token payload.");

        var refreshToken = ApiClient.ReadRefreshCookie(response)
                           ?? throw new InvalidOperationException("Registration did not set a refresh cookie.");

        client.UseBearer(body.AccessToken);
        return (client, new SessionTokens(body.AccessToken, refreshToken, body));
    }
}
