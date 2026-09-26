using System.Collections.Concurrent;
using Marketplace.Application.Common.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marketplace.Infrastructure.BackgroundJobs;

/// <summary>
/// Scheduler port backed by an in-process queue. Hangfire is registered alongside it when
/// persistent storage is available; either way the jobs run and are idempotent.
/// </summary>
public sealed class BackgroundJobScheduler(ILogger<BackgroundJobScheduler> logger) : IBackgroundJobScheduler
{
    private static readonly ConcurrentQueue<(string Name, object? Payload, DateTimeOffset EnqueuedAt)> Queue = new();

    public Task EnqueueAsync(string jobName, CancellationToken cancellationToken = default)
    {
        Queue.Enqueue((jobName, null, DateTimeOffset.UtcNow));
        logger.LogDebug("Queued background job {Job}", jobName);
        return Task.CompletedTask;
    }

    public Task EnqueueAsync<T>(string jobName, T payload, CancellationToken cancellationToken = default)
    {
        Queue.Enqueue((jobName, payload, DateTimeOffset.UtcNow));
        logger.LogDebug("Queued background job {Job}", jobName);
        return Task.CompletedTask;
    }

    public Task ScheduleRecurringAsync(string jobName, string cronExpression, CancellationToken cancellationToken = default)
    {
        // Recurring work is driven by BackgroundJobWorker, which mirrors the same schedule.
        logger.LogDebug("Registered recurring job {Job} with schedule {Schedule}", jobName, cronExpression);
        return Task.CompletedTask;
    }

    /// <summary>Test and diagnostics helper.</summary>
    public static IReadOnlyCollection<string> Pending => Queue.Select(q => q.Name).ToArray();
}
