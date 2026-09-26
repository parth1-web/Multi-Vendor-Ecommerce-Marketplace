using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Auditing;

/// <summary>
/// Immutable record of a consequential action. Kept as a table rather than only as log
/// files so admins can query who changed what, and when.
/// </summary>
public class AuditLog : Entity
{
    private AuditLog()
    {
        Action = AuditAction.Login;
        EntityType = string.Empty;
        ActorEmail = string.Empty;
        CorrelationId = string.Empty;
    }

    private AuditLog(Guid id, Guid? actorId, string actorEmail, AuditAction action, string entityType, Guid? entityId, string? entityName, string? changesJson, string? ipAddress, string? userAgent, string correlationId, DateTimeOffset now)
        : base(id)
    {
        ActorId = actorId;
        ActorEmail = actorEmail;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        EntityName = entityName;
        ChangesJson = changesJson;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        CorrelationId = correlationId;
        CreatedAt = now;
    }

    public Guid? ActorId { get; private set; }

    public string ActorEmail { get; private set; }

    public AuditAction Action { get; private set; }

    public string EntityType { get; private set; }

    public Guid? EntityId { get; private set; }

    public string? EntityName { get; private set; }

    /// <summary>JSON snapshot of the fields that changed. Secrets are redacted before writing.</summary>
    public string? ChangesJson { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public string CorrelationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static AuditLog Record(
        Guid? actorId,
        string actorEmail,
        AuditAction action,
        string entityType,
        Guid? entityId,
        string? entityName,
        string? changesJson,
        string? ipAddress,
        string? userAgent,
        string correlationId,
        DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(action.ToString(), nameof(action));
        Guard.NotNullOrWhiteSpace(entityType, nameof(entityType));
        Guard.NotNullOrWhiteSpace(correlationId, nameof(correlationId));

        return new AuditLog(
            SequentialGuid.New(now),
            actorId,
            actorEmail ?? "anonymous",
            action,
            entityType.Trim(),
            entityId,
            Truncate(entityName, 200),
            changesJson,
            Truncate(ipAddress, 64),
            Truncate(userAgent, 400),
            correlationId,
            now);
    }

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
