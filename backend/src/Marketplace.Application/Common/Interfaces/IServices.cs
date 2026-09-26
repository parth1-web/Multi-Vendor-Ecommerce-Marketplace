using Marketplace.Application.Common.Interfaces;
using Marketplace.Domain.Enums;

namespace Marketplace.Application.Common.Interfaces;

/// <summary>Abstraction over a background scheduler, implemented with Hangfire or an in-process queue.</summary>
public interface IBackgroundJobScheduler
{
    Task EnqueueAsync(string jobName, CancellationToken cancellationToken = default);

    Task EnqueueAsync<T>(string jobName, T payload, CancellationToken cancellationToken = default);

    /// <summary>Registers a recurring job. Duplicate registrations replace the previous schedule.</summary>
    Task ScheduleRecurringAsync(string jobName, string cronExpression, CancellationToken cancellationToken = default);
}

/// <summary>E-mail sender. A no-op implementation is used when SMTP is not configured.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string? TextBody = null, string? ReplyTo = null);

/// <summary>Writes audit entries. Called from the same unit of work as the mutation it describes.</summary>
public interface IAuditService
{
    Task RecordAsync(
        AuditAction action,
        string entityType,
        Guid? entityId,
        string? entityName,
        object? changes,
        CancellationToken cancellationToken = default);
}

/// <summary>Creates notifications and pushes them in real time.</summary>
public interface INotificationService
{
    Task NotifyAsync(Guid userId, NotificationType type, string title, string body, string? link, NotificationAudience audience, CancellationToken cancellationToken = default);

    Task NotifySellerAsync(Guid sellerUserId, NotificationType type, string title, string body, string? link, CancellationToken cancellationToken = default);
}

/// <summary>Password hashing, implemented with PBKDF2 in Infrastructure.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string passwordHash);

    bool NeedsRehash(string passwordHash);
}
