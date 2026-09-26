using System.Text.Json;
using Marketplace.Application.Common.Extensions;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Notifications.Abstractions;
using Marketplace.Application.Modules.Notifications.DTOs;
using Marketplace.Domain.Auditing;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marketplace.Application.Modules.Notifications.Services;

/// <summary>Field names whose values must never reach a log sink or an audit row.</summary>
internal static class AuditRedaction
{
    public static readonly string[] Keys =
    [
        "password", "passwordhash", "passwordconfirm", "token", "refreshtoken",
        "secret", "apikey", "authorization", "cookie", "cardnumber", "cvv"
    ];
}

/// <summary>
/// Creates notifications, persists them and pushes the unread count to the bell in real
/// time. Notifications are the only server state the browser mirrors outside the query
/// cache, and even then only as an invalidation signal.
/// </summary>
public sealed class NotificationService(
    IRepository<Notification> notifications,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    IClock clock,
    IRealtimeNotifier realtime,
    ILogger<NotificationService> logger) : INotificationService, INotificationQueryService
{
    private static readonly string[] RedactedKeys =
    [
        "password", "passwordhash", "passwordconfirm", "token", "refreshtoken",
        "secret", "apikey", "authorization", "cookie", "cardnumber", "cvv"
    ];

    public async Task NotifyAsync(Guid userId, NotificationType type, string title, string body, string? link, NotificationAudience audience, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return;
        }

        var notification = Notification.Create(userId, type, title, body, link, audience, clock.UtcNow);
        await notifications.AddAsync(notification, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await realtime.NotificationCreatedAsync(userId, new
            {
                notification.Id,
                Type = notification.Type.ToString(),
                notification.Title,
                notification.Body,
                notification.Link,
                notification.CreatedAt
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Real-time delivery is best effort; the notification row is the source of truth.
            logger.LogWarning(ex, "Failed to push notification {NotificationId} over SignalR", notification.Id);
        }
    }

    public Task NotifySellerAsync(Guid sellerUserId, NotificationType type, string title, string body, string? link, CancellationToken cancellationToken = default) =>
        NotifyAsync(sellerUserId, type, title, body, link, NotificationAudience.Seller, cancellationToken);

    public async Task<PagedResult<NotificationResponse>> ListAsync(int? page, int? pageSize, bool? unreadOnly, NotificationType? type, CancellationToken cancellationToken = default)
    {
        var paging = new PageRequest(page, pageSize);
        var source = notifications.Query().AsNoTracking().Where(n => n.UserId == currentUser.UserId);

        if (unreadOnly == true)
        {
            source = source.Where(n => !n.IsRead);
        }

        if (type is { } t)
        {
            source = source.Where(n => n.Type == t);
        }

        return await source
            .OrderByDescending(n => n.CreatedAt)
            .ToPagedResultAsync(paging, n => new NotificationResponse(
                n.Id, n.Type, n.Title, n.Body, n.Link, n.Audience, n.IsRead, n.CreatedAt), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<NotificationUnreadCountResponse> GetUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        var unread = await notifications.Query().AsNoTracking()
            .CountAsync(n => n.UserId == currentUser.UserId && !n.IsRead, cancellationToken)
            .ConfigureAwait(false);

        var total = await notifications.Query().AsNoTracking()
            .CountAsync(n => n.UserId == currentUser.UserId, cancellationToken)
            .ConfigureAwait(false);

        return new NotificationUnreadCountResponse(unread, total);
    }

    public async Task<Result> MarkReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var notification = await notifications.Query()
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == currentUser.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (notification is null)
        {
            return Result.Failure("Notification not found.");
        }

        notification.MarkRead(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        var unread = await notifications.Query()
            .Where(n => n.UserId == currentUser.UserId && !n.IsRead)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = clock.UtcNow;
        foreach (var notification in unread)
        {
            notification.MarkRead(now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var notification = await notifications.Query()
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == currentUser.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (notification is null)
        {
            return Result.Failure("Notification not found.");
        }

        notifications.Remove(notification);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>Writes audit rows, redacting any sensitive field before serialising.</summary>
public sealed class AuditService(
    IRepository<AuditLog> auditLogs,
    ICurrentUser currentUser,
    IRequestContext requestContext,
    IClock clock) : IAuditService
{
    public async Task RecordAsync(AuditAction action, string entityType, Guid? entityId, string? entityName, object? changes, CancellationToken cancellationToken = default)
    {
        var entry = AuditLog.Record(
            currentUser.IsAuthenticated ? currentUser.UserId : null,
            currentUser.Email ?? "anonymous",
            action,
            entityType,
            entityId,
            entityName,
            Serialize(changes),
            requestContext.IpAddress,
            requestContext.UserAgent,
            currentUser.CorrelationId,
            clock.UtcNow);

        await auditLogs.AddAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    private static string? Serialize(object? changes)
    {
        if (changes is null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(changes);
        return Redact(json);
    }

    internal static string Redact(string json)
    {
        foreach (var key in AuditRedaction.Keys)
        {
            json = System.Text.RegularExpressions.Regex.Replace(
                json,
                $"\"{key}\"\\s*:\\s*\"[^\"]*\"",
                $"\"{key}\":\"***\"",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return json;
    }
}

/// <summary>Admin-facing audit log search.</summary>
public sealed class AuditQueryService(IRepository<AuditLog> auditLogs) : IAuditQueryService
{
    public async Task<PagedResult<AuditLogResponse>> ListAsync(AuditListQuery query, CancellationToken cancellationToken = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var source = auditLogs.Query().AsNoTracking();

        if (query.Action is { } action)
        {
            source = source.Where(a => a.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            source = source.Where(a => a.EntityType == query.EntityType);
        }

        if (query.EntityId is { } entityId)
        {
            source = source.Where(a => a.EntityId == entityId);
        }

        if (query.ActorId is { } actorId)
        {
            source = source.Where(a => a.ActorId == actorId);
        }

        if (query.From is { } from)
        {
            source = source.Where(a => a.CreatedAt >= from);
        }

        if (query.To is { } to)
        {
            source = source.Where(a => a.CreatedAt <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            source = source.Where(a => EF.Functions.Like(a.ActorEmail, term) || EF.Functions.Like(a.EntityName!, term));
        }

        return await source
            .OrderByDescending(a => a.CreatedAt)
            .ToPagedResultAsync(page, a => new AuditLogResponse(
                a.Id, a.ActorId, a.ActorEmail, a.Action, a.EntityType, a.EntityId, a.EntityName,
                a.ChangesJson, a.IpAddress, a.CorrelationId, a.CreatedAt), cancellationToken)
            .ConfigureAwait(false);
    }
}
