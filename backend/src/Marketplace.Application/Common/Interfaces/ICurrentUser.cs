using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Application.Common.Interfaces;

/// <summary>The authenticated caller, resolved from the JWT. Never built from request input.</summary>
public interface ICurrentUser
{
    /// <summary>User id from the <c>sub</c> claim.</summary>
    Guid UserId { get; }

    string? Email { get; }

    UserRole? Role { get; }

    /// <summary>Seller id from the <c>sid</c> claim. This is the only accepted seller scope.</summary>
    Guid? SellerId { get; }

    /// <summary>
    /// True for an admin. A service deciding whether an unpublished row is readable needs this:
    /// without it a seller-only rule has to become seller-or-admin at every call site, and one
    /// of them will forget.
    /// </summary>
    bool IsAdmin { get; }

    /// <summary>Correlation id for the current request, used by audit and log records.</summary>
    string CorrelationId { get; }

    bool IsAuthenticated { get; }

    bool IsInRole(UserRole role);

    bool IsSeller { get; }
}

/// <summary>Ambient request context (IP, user agent, correlation id).</summary>
public interface IRequestContext
{
    string? IpAddress { get; }

    string? UserAgent { get; }
}
