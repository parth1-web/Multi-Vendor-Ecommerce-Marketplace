using System.Collections.Concurrent;
using Marketplace.Domain.Commissions;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Sellers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Modules.Inventory.Abstractions;
using Marketplace.Application.Modules.Auth.Abstractions;
using Marketplace.Infrastructure.Persistence;

namespace Marketplace.Infrastructure.BackgroundJobs;

/// <summary>Cron expressions for every recurring job, kept in one place.</summary>
public static class JobSchedules
{
    public const string ReleaseExpiredReservations = "* * * * *";
    public const string AbandonedCartCleanup = "0 * * * *";
    public const string LowStockSweep = "*/15 * * * *";
    public const string SellerPayout = "0 2 * * *";
    public const string TemporaryRecordCleanup = "0 3 * * *";
    public const string AnalyticsSnapshot = "0 4 * * *";
}

/// <summary>
/// In-process background worker driven by a timer. It mirrors the Hangfire schedule so
/// behaviour is identical whether the persistent storage is available or not, and every
/// job is written to be idempotent.
/// </summary>
public sealed class BackgroundJobWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<BackgroundJobWorker> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastRun = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Background job worker started.");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
                await RunDueJobsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failing job must never kill the worker.
                logger.LogError(ex, "Background job tick failed");
            }
        }

        logger.LogInformation("Background job worker stopped.");
    }

    private async Task RunDueJobsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        await RunDailyJobsAsync(now, cancellationToken).ConfigureAwait(false);

        if (IsDue(JobSchedules.ReleaseExpiredReservations, now))
        {
            await RunAsync("release-expired-reservations", JobSchedules.ReleaseExpiredReservations, now, async services =>
            {
                var inventory = services.GetRequiredService<IInventoryService>();
                return await inventory.ReleaseExpiredReservationsAsync(cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        if (IsDue(JobSchedules.LowStockSweep, now))
        {
            await RunAsync("low-stock-sweep", JobSchedules.LowStockSweep, now, _ => Task.FromResult(0), cancellationToken).ConfigureAwait(false);
        }

        if (IsDue(JobSchedules.AbandonedCartCleanup, now))
        {
            await RunAsync("abandoned-cart-cleanup", JobSchedules.AbandonedCartCleanup, now, _ => Task.FromResult(0), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunDailyJobsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        if (now.Hour < 2 || _lastRun.ContainsKey("daily"))
        {
            return;
        }

        _lastRun["daily"] = now;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var payouts = scope.ServiceProvider.GetRequiredService<SellerPayoutProcessor>();
            await payouts.ProcessAsync(today.AddDays(-1), today, cancellationToken).ConfigureAwait(false);

            var cleanup = scope.ServiceProvider.GetRequiredService<TemporaryRecordCleanup>();
            await cleanup.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Daily maintenance failed");
        }
    }

    private bool IsDue(string schedule, DateTimeOffset now)
    {
        var minutes = schedule switch
        {
            "* * * * *" => 1,
            "*/15 * * * *" => 15,
            "0 * * * *" => 60,
            _ => 60
        };

        var key = schedule;
        var last = _lastRun.GetOrAdd(key, DateTimeOffset.MinValue);

        if (now - last < TimeSpan.FromMinutes(minutes))
        {
            return false;
        }

        _lastRun[key] = now;
        return true;
    }

    private async Task RunAsync(string name, string schedule, DateTimeOffset now, Func<IServiceProvider, Task<int>> work, CancellationToken cancellationToken)
    {
        if (!_lastRun.ContainsKey(schedule))
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var affected = await work(scope.ServiceProvider).ConfigureAwait(false);
            logger.LogInformation("Job {Job} completed with {Affected} affected row(s)", name, affected);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {Job} failed", name);
        }
    }
}

/// <summary>Converts accrued commissions into payout statements. Safe to re-run per period.</summary>
public sealed class SellerPayoutProcessor(
    MarketplaceDbContext context,
    IClock clock,
    ILogger<SellerPayoutProcessor> logger)
{
    public async Task<int> ProcessAsync(DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        var eligible = await context.Commissions
            .Where(c => c.Status == CommissionStatus.Accrued && c.CreatedAt >= periodStart && c.CreatedAt < periodEnd)
            .GroupBy(c => c.SellerId)
            .Select(g => new
            {
                SellerId = g.Key,
                Count = g.Count(),
                Gross = g.Sum(c => c.GrossAmount),
                Commission = g.Sum(c => c.CommissionAmount)
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var created = 0;

        foreach (var group in eligible)
        {
            var reference = $"PO-{periodEnd:yyyyMMdd}-{group.SellerId.ToString()[..6].ToUpperInvariant()}";

            if (await context.SellerPayouts.AnyAsync(p => p.Reference == reference, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var payout = SellerPayout.Create(
                group.SellerId,
                reference,
                group.Gross,
                group.Commission,
                group.Count,
                periodStart,
                periodEnd,
                now);

            context.SellerPayouts.Add(payout);

            var commissions = await context.Commissions
                .Where(c => c.SellerId == group.SellerId && c.Status == CommissionStatus.Accrued &&
                            c.CreatedAt >= periodStart && c.CreatedAt < periodEnd)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var commission in commissions)
            {
                commission.MarkPaid(payout.Id, now);
            }

            created++;
        }

        if (created > 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Generated {Count} seller payout statement(s)", created);
        }

        return created;
    }
}

/// <summary>Purges expired refresh tokens and consumed webhook records.</summary>
public sealed class TemporaryRecordCleanup(MarketplaceDbContext context, IClock clock, ILogger<TemporaryRecordCleanup> logger)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var removed = 0;

        var expiredTokens = await context.RefreshTokens
            .Where(t => t.ExpiresAt < now.AddDays(-7))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        removed += expiredTokens;

        var consumedWebhooks = await context.PaymentWebhooks
            .Where(w => w.ReceivedAt < now.AddDays(-90))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        removed += consumedWebhooks;

        var readNotifications = await context.Notifications
            .Where(n => n.IsRead && n.CreatedAt < now.AddMonths(-12))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        removed += readNotifications;

        if (removed > 0)
        {
            logger.LogInformation("Cleaned up {Count} temporary record(s)", removed);
        }

        return removed;
    }
}