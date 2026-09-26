using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Notifications;

/// <summary>A user-facing notification. Read state is tracked so the badge can be served cheaply.</summary>
public class Notification : Entity
{
    private Notification()
    {
        Title = string.Empty;
        Body = string.Empty;
    }

    private Notification(Guid id, Guid userId, NotificationType type, string title, string body, string? link, NotificationAudience audience, DateTimeOffset now)
        : base(id)
    {
        UserId = userId;
        Type = type;
        Title = title;
        Body = body;
        Link = link;
        Audience = audience;
        CreatedAt = now;
    }

    public Guid UserId { get; private set; }

    public NotificationType Type { get; private set; }

    public string Title { get; private set; }

    public string Body { get; private set; }

    /// <summary>Relative route the notification deep-links to, e.g. <c>/orders/MP-...</c>.</summary>
    public string? Link { get; private set; }

    public NotificationAudience Audience { get; private set; }

    public bool IsRead { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Notification Create(Guid userId, NotificationType type, string title, string body, string? link, NotificationAudience audience, DateTimeOffset now)
    {
        Guard.NotEmpty(userId, nameof(userId));
        Guard.NotNullOrWhiteSpace(title, nameof(title));
        Guard.NotNullOrWhiteSpace(body, nameof(body));

        return new Notification(
            SequentialGuid.New(now),
            userId,
            type,
            title.Trim(),
            body.Trim(),
            string.IsNullOrWhiteSpace(link) ? null : link.Trim(),
            audience,
            now);
    }

    public void MarkRead(DateTimeOffset now)
    {
        if (IsRead)
        {
            return;
        }

        IsRead = true;
        ReadAt = now;
    }
}
