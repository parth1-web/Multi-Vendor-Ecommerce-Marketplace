namespace Marketplace.Domain.Common;

/// <summary>
/// Marker for something that happened in the domain and that other parts of the
/// system may react to (notifications, audit entries, real-time pushes, background jobs).
/// </summary>
public interface IDomainEvent
{
    /// <summary>Stable identifier, used to de-duplicate handlers.</summary>
    Guid EventId { get; }

    /// <summary>Moment the event occurred (UTC).</summary>
    DateTimeOffset OccurredOn { get; }

    /// <summary>Logical event name, e.g. <c>OrderCreated</c>.</summary>
    string EventName { get; }
}

/// <summary>Convenience base implementation of <see cref="IDomainEvent"/>.</summary>
public abstract record DomainEvent : IDomainEvent
{
    protected DomainEvent(Guid eventId, DateTimeOffset occurredOn)
    {
        EventId = eventId;
        OccurredOn = occurredOn;
    }

    public Guid EventId { get; }

    public DateTimeOffset OccurredOn { get; }

    public abstract string EventName { get; }
}
