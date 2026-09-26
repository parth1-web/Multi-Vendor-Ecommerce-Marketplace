using Marketplace.Domain.Common;

namespace Marketplace.Domain.Identity;

/// <summary>
/// A single-use password reset token.
///
/// Only the SHA-256 hash is stored, exactly as with a refresh token: a database that is read by
/// someone who is not the user must not hand them the ability to take the account over. One
/// token replaces any earlier unused one, so a second request cannot be used alongside the
/// first, and using one revokes every session the account had.
/// </summary>
public class PasswordResetToken : Entity
{
    private PasswordResetToken()
    {
        TokenHash = string.Empty;
    }

    private PasswordResetToken(Guid id, Guid userId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt, string? createdByIp)
        : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        CreatedByIp = createdByIp;
    }

    public Guid UserId { get; private set; }

    /// <summary>SHA-256 of the raw token. The raw token is never persisted.</summary>
    public string TokenHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public string? CreatedByIp { get; private set; }

    public bool IsUsed => UsedAt is not null;

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public bool IsUsable(DateTimeOffset now) => !IsUsed && !IsExpired(now);

    public static PasswordResetToken Create(Guid userId, string tokenHash, DateTimeOffset now, TimeSpan lifetime, string? createdByIp)
    {
        Guard.NotEmpty(userId, nameof(userId));
        Guard.NotNullOrWhiteSpace(tokenHash, nameof(tokenHash));

        return new PasswordResetToken(SequentialGuid.New(), userId, tokenHash, now, now.Add(lifetime), createdByIp);
    }

    /// <summary>Burns the token. A spent token stays on the row as the record of what happened.</summary>
    public void Use(DateTimeOffset now)
    {
        if (IsUsed)
        {
            throw new BusinessRuleException("This password reset link has already been used.");
        }

        UsedAt = now;
    }
}
