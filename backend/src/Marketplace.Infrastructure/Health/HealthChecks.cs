using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Modules.Auth.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Marketplace.Infrastructure.Health;

/// <summary>Reports whether the distributed cache is actually reachable.</summary>
public sealed class CacheHealthCheck(ICacheService cache, ILogger<CacheHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var probe = await cache.GetAsync<string>($"health:probe:{Guid.NewGuid():N}", cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy("Cache is reachable.", data: new Dictionary<string, object>
            {
                ["probe"] = probe ?? "null"
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache health probe failed");
            return HealthCheckResult.Degraded("Cache is unreachable; serving from the database.");
        }
    }
}

/// <summary>Readiness of the background worker heartbeat.</summary>
public sealed class BackgroundWorkerHealthCheck(IHealthCheckWorkerState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var age = DateTimeOffset.UtcNow - state.LastTick;

        return Task.FromResult(age <= TimeSpan.FromMinutes(5)
            ? HealthCheckResult.Healthy("Background worker is ticking.")
            : HealthCheckResult.Degraded($"Background worker has not ticked for {age.TotalMinutes:F1} minute(s)."));
    }
}

/// <summary>Tracks the background worker heartbeat; the API reads it for readiness.</summary>
public sealed class IHealthCheckWorkerState
{
    private long _lastTicks = DateTimeOffset.UtcNow.UtcTicks;

    public DateTimeOffset LastTick => new(Interlocked.Read(ref _lastTicks), TimeSpan.Zero);

    public void Beat() => Interlocked.Exchange(ref _lastTicks, DateTimeOffset.UtcNow.UtcTicks);
}
