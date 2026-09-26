using Microsoft.AspNetCore.Authorization;
using System.Reflection;
using System.Text;
using Marketplace.API.Hubs;
using Marketplace.API.Middleware;
using Marketplace.API.Security;
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
    builder.Services.AddScoped<DatabaseSeeder>();

    builder.Services.Configure<ApiBehaviorOptions>(options =>
    {
        options.SuppressModelStateInvalidFilter = false;
    });

    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        });

    builder.Services.AddProblemDetails();

    // ---- authentication -------------------------------------------------------
    var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
    ValidateJwtConfiguration(jwtOptions, builder.Environment.IsProduction());

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = builder.Environment.IsProduction();
            options.SaveToken = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtOptions.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(jwtOptions.ClockSkewSeconds),
                NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier,
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
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.AddFixedWindowLimiter("global", limiter =>
        {
            limiter.PermitLimit = 3000;
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.QueueLimit = 0;
            limiter.AutoReplenishment = true;
        });

        options.AddFixedWindowLimiter("auth", limiter =>
        {
            limiter.PermitLimit = 20;
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.QueueLimit = 0;
        });

        options.AddFixedWindowLimiter("checkout", limiter =>
        {
            limiter.PermitLimit = 40;
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.QueueLimit = 0;
        });

        options.AddFixedWindowLimiter("webhook", limiter =>
        {
            limiter.PermitLimit = 600;
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.QueueLimit = 0;
        });
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
        if (app.Environment.IsProduction())
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
