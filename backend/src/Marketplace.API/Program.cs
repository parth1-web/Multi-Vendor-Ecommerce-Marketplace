using Microsoft.AspNetCore.Authorization;
using System.Reflection;
using System.Text;
using Marketplace.API.Configuration;
using Marketplace.API.Security;
using Marketplace.API.Hubs;
using Marketplace.API.Middleware;
using Marketplace.Application;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Modules.Auth.Abstractions;
using Marketplace.Application.Modules.Auth.Services;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure;
using Marketplace.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Serilog — structured logging with redaction-friendly conventions
// ---------------------------------------------------------------------------
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Marketplace.API")
    .WriteTo.Console(new RenderedCompactJsonFormatter())
    .CreateLogger();

builder.Host.UseSerilog();

try
{
    // ---------------------------------------------------------------------------
    // Services
    // ---------------------------------------------------------------------------
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, CurrentUser>();
    builder.Services.AddScoped<IRequestContext, RequestContext>();

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();
    var validationClock = new ApplicationValidationClock();
    builder.Services.AddSingleton(validationClock);

    builder.Services.AddScoped<DatabaseSeeder>();

    builder.Services.Configure<ApiBehaviorOptions>(options =>
    {
        options.SuppressModelStateInvalidFilter = false;
    });

    builder.Services.AddControllers(options => options.Filters.Add<FluentValidationFilter>())
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;

            // Enums travel as their names. A status of 2 tells a client nothing about what it is
            // and nothing about what it is allowed to become, and the ordinals are an
            // implementation detail that changes the moment anyone reorders the enum.
            options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });

    builder.Services.AddProblemDetails();

    // ---- authentication -------------------------------------------------------
    var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
    ValidateJwtConfiguration(jwtOptions, builder.Environment.IsProduction());

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer();

    // The bearer parameters are configured from the resolved options rather than from a
    // value captured here. Options are bound when they are first resolved, which is the only
    // point at which every configuration source (including ones added by a test host) is
    // guaranteed to be present; an eagerly read value can silently disagree with the key the
    // token service signs with.
    builder.Services.AddOptions<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme)
        .Configure<Microsoft.Extensions.Options.IOptions<JwtOptions>>((options, configured) =>
        {
            var jwt = configured.Value;

            options.RequireHttpsMetadata = builder.Environment.IsProduction();
            options.SaveToken = false;

            // Claims are consumed exactly as the token service writes them ("sub", "role",
            // "sid"). Inbound mapping would rename them to the long WS-Federation URIs, and
            // every role-restricted endpoint would then deny an authorised caller.
            options.MapInboundClaims = false;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = jwt.Audience,
                ValidateIssuerSigningKey = true,
                // The same key id the token is signed with, so the validator can match it.
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)) { KeyId = TokenService.SigningKeyId },
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(jwt.ClockSkewSeconds),
                // Token lifetimes are stamped with the application clock, so they are judged by it
                // too. Using the system clock here would reject tokens the API just issued
                // whenever the application clock is frozen, shifted or under test.
                LifetimeValidator = (notBefore, expires, _, parameters) =>
                {
                    var now = validationClock.UtcNow.UtcDateTime;
                    return now.Add(parameters.ClockSkew) >= notBefore && now.Subtract(parameters.ClockSkew) <= expires;
                },
                NameClaimType = "sub",
                RoleClaimType = TokenService.RoleClaim
            };

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    // The refresh token lives in an HttpOnly cookie; the access token does not.
                    return Task.CompletedTask;
                }
            };
        });


    builder.Services.AddScoped<IAuthorizationHandler, SellerStatusAuthorizationHandler>();
    builder.Services.AddAuthorization(options => AuthorizationPolicies.Configure(options));

    // ---- CORS -----------------------------------------------------------------
    var corsOptions = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
    var allowedOrigins = corsOptions.AllowedOrigins.Where(o => !string.IsNullOrWhiteSpace(o)).ToArray();

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            if (allowedOrigins.Length == 0)
            {
                // No allow-list configured: refuse cross-origin entirely rather than
                // falling back to a wildcard that would be unsafe with credentials.
                return;
            }

            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
                .WithExposedHeaders("X-Correlation-Id", "X-Total-Count")
                .SetPreflightMaxAge(TimeSpan.FromHours(1));
        });
    });

    // ---- rate limiting --------------------------------------------------------
    // Limits are bound through options so they are read when the policy is first resolved.
    // A value captured here would miss any configuration source registered later, and the
    // limiter would then enforce numbers nobody configured.
    builder.Services.AddOptions<RateLimitOptions>()
        .Bind(builder.Configuration.GetSection(RateLimitOptions.SectionName));

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.AddPolicy<string>(RateLimitPolicies.Global, context => RateLimitPartitionFactory.FixedWindow(
            context, static _ => new RateLimitOptions().GlobalPermitLimit));

        options.AddPolicy<string>(RateLimitPolicies.Auth, context => RateLimitPartitionFactory.FixedWindow(
            context, limits => limits.AuthPermitLimit));

        options.AddPolicy<string>(RateLimitPolicies.Checkout, context => RateLimitPartitionFactory.FixedWindow(
            context, limits => limits.CheckoutPermitLimit));

        options.AddPolicy<string>(RateLimitPolicies.Webhook, context => RateLimitPartitionFactory.FixedWindow(
            context, limits => limits.WebhookPermitLimit));
    });

    // ---- SignalR --------------------------------------------------------------
    builder.Services.AddSignalR(options => options.EnableDetailedErrors = builder.Environment.IsDevelopment());

    var redis = builder.Configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();
    if (redis.Enabled && !string.IsNullOrWhiteSpace(redis.ConnectionString))
    {
        builder.Services.AddSignalR()
            .AddStackExchangeRedis(options => options.Configuration = StackExchange.Redis.ConfigurationOptions.Parse(redis.ConnectionString));
    }

    // ---- Swagger --------------------------------------------------------------
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Marketplace API",
            Version = "v1",
            Description = "Multi-vendor marketplace API. Authentication is JWT bearer; all seller and customer data is scoped server-side."
        });

        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste only the access token. The refresh token is an HttpOnly cookie."
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
        });

        var xml = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xml);
        if (File.Exists(xmlPath))
        {
            options.IncludeXmlComments(xmlPath);
        }
    });

    var app = builder.Build();

    // Token validation reads the same application clock that stamps the tokens.
    validationClock.Attach(app.Services);

    // ---------------------------------------------------------------------------
    // Pipeline
    // ---------------------------------------------------------------------------
    app.UseExceptionHandler();
    app.UseMiddleware<RequestLoggingMiddleware>();
    app.UseMiddleware<SecurityHeadersMiddleware>();

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
        app.UseHttpsRedirection();
    }

    app.UseCors();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Marketplace API v1"));
    }

    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHub<MarketplaceHub>(MarketplaceHub.Route);

    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        Predicate = _ => false,
        ResponseWriter = WriteHealthResponseAsync
    });

    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = WriteHealthResponseAsync
    });

    await SeedAsync(app).ConfigureAwait(false);

    await app.RunAsync().ConfigureAwait(false);

    static async Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                durationMs = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
                error = e.Value.Exception?.Message
            })
        };

        await context.Response.WriteAsJsonAsync(payload).ConfigureAwait(false);
    }

    static async Task SeedAsync(WebApplication app)
    {
        // Integration tests build their own deterministic world, so the demo seed must not
        // run there. Production keeps the previous behaviour of relying on migrations only.
        if (app.Environment.IsProduction() || app.Environment.IsEnvironment("Testing"))
        {
            return;
        }

        try
        {
            using var scope = app.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<Marketplace.Infrastructure.Persistence.MarketplaceDbContext>();
            var redisOptions = scope.ServiceProvider.GetRequiredService<IOptions<RedisOptions>>().Value;

            // Only touch the database when it is actually reachable, so the app still
            // starts for a frontend-only preview or a health probe.
            if (!await context.Database.CanConnectAsync().ConfigureAwait(false))
            {
                app.Logger.LogWarning("Database is not reachable; skipping seed data.");
                return;
            }

            await context.Database.MigrateAsync().ConfigureAwait(false);
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync().ConfigureAwait(false);
            _ = redisOptions;
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Database migration or seeding was skipped.");
        }
    }

    static void ValidateJwtConfiguration(JwtOptions options, bool isProduction)
    {
        if (string.IsNullOrWhiteSpace(options.Key) || options.Key.Length < 32)
        {
            if (isProduction)
            {
                throw new InvalidOperationException("Jwt:Key must be configured with at least 32 characters in Production.");
            }

            return;
        }

        if (isProduction && options.Key.Contains("development-only", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Jwt:Key must not use the development default in Production.");
        }
    }
}
catch (Exception ex)
{
    Log.Fatal(ex, "The marketplace API terminated unexpectedly.");
    await Log.CloseAndFlushAsync();
    throw;
}

/// <summary>Exposed so <c>WebApplicationFactory</c> can boot the API in integration tests.</summary>
public partial class Program;
