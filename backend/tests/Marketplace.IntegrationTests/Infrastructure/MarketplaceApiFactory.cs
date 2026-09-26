using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Modules.Auth.Abstractions;
using Marketplace.Application.Modules.Auth.DTOs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Marketplace.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API pipeline against a real PostgreSQL database, a frozen clock, an
/// in-process cache and a recording realtime notifier. Each factory owns a private database
/// that is created, migrated and dropped around the run, so tests exercise the same provider,
/// the same generated SQL and the same migrations as production without touching real data.
/// </summary>
public class MarketplaceApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Environment variable that overrides the server the test databases are created on.</summary>
    public const string ServerVariable = "MARKETPLACE_TEST_POSTGRES";

    private bool _databaseReady;

    /// <summary>Name of the private database this factory owns.</summary>
    private readonly string _databaseName = $"marketplace_test_{Guid.NewGuid():N}";

    /// <summary>Connection string for the throwaway database under test.</summary>
    private string? _connectionString;

    /// <summary>Connection string used to create and drop the throwaway database.</summary>
    private string? _controlConnectionString;

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
            // The app's own configuration is already registered here, so the test server
            // credentials come from the same place a developer runs the API from. An
            // environment variable wins for CI, where the password is a secret.
            var loaded = config.Build();
            var configured = loaded["ConnectionStrings:DefaultConnection"];
            var server = Environment.GetEnvironmentVariable(ServerVariable)
                         ?? configured

                         ?? throw new InvalidOperationException(
                             $"No PostgreSQL connection string. Set ConnectionStrings:DefaultConnection or the {ServerVariable} environment variable.");

            var target = new NpgsqlConnectionStringBuilder(server) { Database = _databaseName };
            _connectionString = target.ConnectionString;
            _controlConnectionString = new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString;

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
            // Set MARKETPLACE_TEST_SQL=1 to see the statements a failing scenario issued.
            if (Environment.GetEnvironmentVariable("MARKETPLACE_TEST_SQL") == "1")
            {
                services.AddDbContext<Marketplace.Infrastructure.Persistence.MarketplaceDbContext>((provider, options) =>
                {
                    options.UseNpgsql(_connectionString);
                    var logPath = Path.Combine(Path.GetTempPath(), "marketplace-test-sql.log");
                    options
                        .LogTo(message => File.AppendAllText(logPath, message + Environment.NewLine), LogLevel.Information)
                        .EnableSensitiveDataLogging();
                });
            }

            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);

            services.RemoveAll<IRealtimeNotifier>();
            services.AddSingleton<IRealtimeNotifier, RecordingRealtimeNotifier>();

            // No SMTP server in a test run, so e-mail is captured instead of skipped. A test that
            // needs to follow a link from an inbox has to be able to read the inbox.
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender, RecordingEmailSender>();
        });

    }

    /// <summary>
    /// Creates this factory's private database, brings it up to date with the real migrations
    /// and returns a seeder bound to a scope that stays alive for the duration of the class.
    /// Applying the migrations rather than EnsureCreated means the suite also proves the
    /// generated PostgreSQL schema is valid.
    /// </summary>
    public async Task<MarketplaceTestData> CreateDatabaseAsync()
    {
        if (_databaseReady)
        {
            return new MarketplaceTestData(Services, Clock);
        }

        // Touching Services builds the host, which is what runs the configuration callback
        // that works out which server and database this factory owns.
        _ = Services;

        await CreateDatabaseOnServerAsync(CancellationToken.None).ConfigureAwait(false);

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Marketplace.Infrastructure.Persistence.MarketplaceDbContext>();

        await context.Database.MigrateAsync().ConfigureAwait(false);

        _databaseReady = true;
        return new MarketplaceTestData(Services, Clock);
    }

    private async Task CreateDatabaseOnServerAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(RequireControlConnectionString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{_databaseName}\"";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task DropDatabaseOnServerAsync(CancellationToken cancellationToken)
    {
        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(RequireControlConnectionString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private string RequireControlConnectionString() =>
        _controlConnectionString
        ?? throw new InvalidOperationException(
            "The test host was never configured, so there is no server to create a database on.");


    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing || _controlConnectionString is null)
        {
            return;
        }

        try
        {
            DropDatabaseOnServerAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // A database that cannot be dropped must never fail an otherwise green run; it is
            // named after the run, so a later clean-up can remove it.
            Console.WriteLine($"Could not drop test database {_databaseName}: {ex.Message}");
        }
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
    /// <summary>
    /// The API's own JSON conventions, so a test reads what a real client would. Enums in
    /// particular: the API sends names, and a test that deserialised them as ordinals would
    /// quietly accept a payload the product does not send.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public HttpClient Http { get; } = client;

    public async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken = default) =>
        await Http.GetFromJsonAsync<T>(url, Json, cancellationToken).ConfigureAwait(false);

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

/// <summary>
/// Captures e-mail instead of sending it, so a test can follow a link the way a user would.
/// </summary>
public sealed class RecordingEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _messages = [];

    public IReadOnlyList<EmailMessage> Messages
    {
        get
        {
            lock (_messages)
            {
                return _messages.ToList();
            }
        }
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        lock (_messages)
        {
            _messages.Add(message);
        }

        return Task.CompletedTask;
    }

    /// <summary>The single message sent to an address, or null when there was not exactly one.</summary>
    public EmailMessage? For(string to) =>
        Messages.LastOrDefault(m => string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase));
}