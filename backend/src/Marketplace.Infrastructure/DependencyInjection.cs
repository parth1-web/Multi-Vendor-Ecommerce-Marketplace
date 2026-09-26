using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Modules.Auth.Abstractions;
using Marketplace.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Marketplace.Infrastructure;

/// <summary>
/// Single registration point for Infrastructure: EF Core, the cache, payment gateways,
/// background processing, e-mail and health checks.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.Configure<MarketplaceOptions>(configuration.GetSection(MarketplaceOptions.SectionName));
        services.Configure<PaymentOptions>(configuration.GetSection(PaymentOptions.SectionName));
        services.Configure<CorsOptions>(configuration.GetSection(CorsOptions.SectionName));
        services.Configure<Services.SmtpOptions>(configuration.GetSection(Services.SmtpOptions.SectionName));

        services.AddDbContext<Persistence.MarketplaceDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(Persistence.MarketplaceDbContext).Assembly.FullName);
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, TimeSpan.FromSeconds(5), null);
            });

            if (configuration.GetValue("Database:EnableSensitiveLogging", false))
            {
                options.EnableSensitiveDataLogging();
            }
        });

        AddCache(services, configuration);
        AddPaymentGateways(services);
        AddBackgroundProcessing(services);
        AddHealthChecks(services);

        services.AddSingleton<IEmailSender, Services.SmtpEmailSender>();
        services.AddScoped(typeof(IRepository<>), typeof(Repositories.Repository<>));
        services.AddScoped<IUnitOfWork, Repositories.UnitOfWork>();

        return services;
    }

    private static void AddCache(IServiceCollection services, IConfiguration configuration)
    {
        var redis = configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();
        var redisEnabled = redis.Enabled && !string.IsNullOrWhiteSpace(redis.ConnectionString);

        if (redisEnabled)
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redis.ConnectionString;
                options.InstanceName = redis.InstancePrefix;
            });

            services.AddSingleton<ICacheService, Caching.DistributedCacheService>();
        }
        else
        {
            // No Redis: the application stays fully functional on a process-local cache.
            services.AddSingleton<ICacheService, Caching.InMemoryCacheService>();
        }
    }

    private static void AddPaymentGateways(IServiceCollection services)
    {
        // Mock and cash-on-delivery need no HTTP client.
        services.AddSingleton<IPaymentGateway, CashOnDeliveryGateway>();
        services.AddSingleton<MockPaymentGateway>();
        services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<MockPaymentGateway>());

        // Real gateways use typed clients so timeouts and logging come from the factory.
        services.AddHttpClient<Payments.KhaltiPaymentGateway>();
        services.AddHttpClient<Payments.EsewaPaymentGateway>();
        services.AddHttpClient<Payments.StripePaymentGateway>();

        services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<Payments.KhaltiPaymentGateway>());
        services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<Payments.EsewaPaymentGateway>());
        services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<Payments.StripePaymentGateway>());

        services.AddSingleton<IPaymentGatewayResolver, Payments.PaymentGatewayResolver>();
    }

    private static void AddBackgroundProcessing(IServiceCollection services)
    {
        services.AddSingleton<BackgroundJobs.BackgroundJobScheduler>();
        services.AddSingleton<IBackgroundJobScheduler>(sp => sp.GetRequiredService<BackgroundJobs.BackgroundJobScheduler>());
        services.AddScoped<BackgroundJobs.SellerPayoutProcessor>();
        services.AddScoped<BackgroundJobs.TemporaryRecordCleanup>();
        services.AddHostedService<BackgroundJobs.BackgroundJobWorker>();
    }

    private static void AddHealthChecks(IServiceCollection services)
    {
        services.AddSingleton<Health.IHealthCheckWorkerState>();
        services.AddHealthChecks()
            .AddDbContextCheck<Persistence.MarketplaceDbContext>("postgres", tags: ["ready", "db"])
            .AddCheck<Health.CacheHealthCheck>("cache", tags: ["ready", "cache"])
            .AddCheck<Health.BackgroundWorkerHealthCheck>("background-worker", tags: ["ready"]);
    }
}
