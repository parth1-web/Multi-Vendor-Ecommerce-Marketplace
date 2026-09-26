using System.Security.Claims;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Modules.Auth.Services;
using Marketplace.Domain.Enums;

namespace Marketplace.API.Security;

/// <summary>
/// Resolves the authenticated caller from the JWT. Every service that needs a seller
/// scope or the caller's identity gets it from here — never from a request body.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    /// <summary>Claim carrying the correlation id issued by the request-logging middleware.</summary>
    public const string CorrelationClaim = "cid";

    public ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid UserId
    {
        get
        {
            var value = Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? Principal?.FindFirstValue("sub");
            return Guid.TryParse(value, out var id) ? id : Guid.Empty;
        }
    }

    public string? Email =>
        Principal?.FindFirstValue(ClaimTypes.Email)
        ?? Principal?.FindFirstValue("email");

    public UserRole? Role
    {
        get
        {
            var value = Principal?.FindFirstValue(TokenService.RoleClaim)
                        ?? Principal?.FindFirstValue(ClaimTypes.Role);
            return Enum.TryParse<UserRole>(value, ignoreCase: true, out var role) ? role : null;
        }
    }

    public Guid? SellerId
    {
        get
        {
            var value = Principal?.FindFirstValue(TokenService.SellerClaim);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string CorrelationId =>
        accessor.HttpContext?.Items["CorrelationId"]?.ToString()
        ?? Principal?.FindFirstValue(CorrelationClaim)
        ?? "unknown";

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public bool IsInRole(UserRole role) =>
        string.Equals(Role?.ToString(), role.ToString(), StringComparison.OrdinalIgnoreCase);

    public bool IsAdmin => IsInRole(UserRole.Admin) || IsInRole(UserRole.SuperAdmin);

    public bool IsSeller => IsInRole(UserRole.Seller);
}

/// <summary>Exposes the client IP and user agent to the audit writer.</summary>
public sealed class RequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    public string? IpAddress =>
        accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var agent = accessor.HttpContext?.Request.Headers.UserAgent.ToString();
            return string.IsNullOrWhiteSpace(agent) ? null : agent.Length > 400 ? agent[..400] : agent;
        }
    }
}
