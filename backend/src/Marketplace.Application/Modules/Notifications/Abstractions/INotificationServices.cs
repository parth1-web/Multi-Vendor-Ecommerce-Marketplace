using Marketplace.Application.Common.Extensions;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Notifications.DTOs;
using Marketplace.Domain.Auditing;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marketplace.Application.Modules.Notifications.Abstractions;

public interface INotificationQueryService
{
    Task<PagedResult<NotificationResponse>> ListAsync(int? page, int? pageSize, bool? unreadOnly, NotificationType? type, CancellationToken cancellationToken = default);

    Task<NotificationUnreadCountResponse> GetUnreadCountAsync(CancellationToken cancellationToken = default);

    Task<Result> MarkReadAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> MarkAllReadAsync(CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IAuditQueryService
{
    Task<PagedResult<AuditLogResponse>> ListAsync(AuditListQuery query, CancellationToken cancellationToken = default);
}

public sealed record AuditListQuery(
    int? Page,
    int? PageSize,
    AuditAction? Action,
    string? EntityType,
    Guid? EntityId,
    Guid? ActorId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Search);
