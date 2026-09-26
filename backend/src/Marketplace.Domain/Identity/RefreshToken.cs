using Marketplace.Domain.Common;

namespace Marketplace.Domain.Identity;

/// <summary>
/// A rotating, single-use refresh token. Only the SHA-256 hash is stored, and every
/// token in a family shares a <see cref="FamilyId"/> so replaying a rotated token can
/// revoke the whole family.
/// </summary>
public class RefreshToken : Entity
{
    private RefreshToken()
    {
        TokenHash = string.Empty;
        FamilyId = Guid.Empty;
    }

    private RefreshToken(Guid id, Guid userId, string tokenHash, Guid familyId, DateTimeOffset createdAt, DateTimeOffset expiresAt)
        : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        FamilyId = familyId;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid UserId { get; private set; }

    /// <summary>SHA-256 of the raw token. The raw token is never persisted.</summary>
    public string TokenHash { get; private set; }

    /// <summary>Groups every token descending from a single login.</summary>
    public Guid FamilyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevokedReason { get; private set; }

    public string? CreatedByIp { get; private set; }

    public string? UserAgent { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public bool IsActive(DateTimeOffset now) => !IsRevoked && !IsExpired(now);

    /// <summary>Id of the token that replaced this one during rotation.</summary>
    public Guid? RevokedByTokenId { get; private set; }

    public static RefreshToken Create(
        Guid userId,
        string tokenHash,
        Guid familyId,
        DateTimeOffset now,
        int lifetimeInDays,
        string? ipAddress,
        string? userAgent)
    {
        Guard.NotEmpty(userId, nameof(userId));
        Guard.NotNullOrWhiteSpace(tokenHash, nameof(tokenHash));

        return new RefreshToken(
            SequentialGuid.New(),
            userId,
            tokenHash,
            familyId == Guid.Empty ? SequentialGuid.New() : familyId,
            now,
            now.AddDays(lifetimeInDays));
    }

    private static RefreshToken CreateWithExpiry(Guid userId, string tokenHash, Guid familyId, DateTimeOffset createdAt, DateTimeOffset expiresAt, string? ip, string? agent) =>
        new(SequentialGuid.New(), userId, tokenHash, familyId, createdAt, expiresAt)
        {
            CreatedByIp = ip,
            UserAgent = agent
        };

    /// <summary>Rotates this token into a successor, revoking the current one.</summary>
    public RefreshToken RotateTo(string newTokenHash, DateTimeOffset now)
    {
        if (IsRevoked)
        {
            throw new BusinessRuleException("A revoked refresh token cannot be rotated.");
        }

        if (IsExpired(now))
        {
            throw new BusinessRuleException("An expired refresh token cannot be rotated.");
        }

        var successor = CreateWithExpiry(UserId, newTokenHash, FamilyId, now, ExpiresAt, CreatedByIp, UserAgent);
        RevokedAt = now;
        RevokedReason = "rotated";
        RevokedByTokenId = successor.Id;
        return successor;
    }

    public void Revoke(DateTimeOffset now, string reason)
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = reason;
    }
}
