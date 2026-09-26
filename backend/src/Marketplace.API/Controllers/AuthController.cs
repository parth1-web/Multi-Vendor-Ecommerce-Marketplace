using Marketplace.API.Middleware;
using Marketplace.API.Security;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Modules.Auth.Abstractions;
using Marketplace.Application.Modules.Auth.DTOs;
using Marketplace.Application.Modules.Auth.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Marketplace.API.Controllers;

/// <summary>
/// Authentication endpoints. Registration and login are rate limited; the refresh token
/// is delivered as an HttpOnly cookie and only the access token is returned in the body.
/// </summary>
[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController(
    IAuthService auth,
    ICurrentUser currentUser,
    IRequestContext requestContext) : ControllerBase
{
    private const string RefreshCookieName = "mp_refresh";

    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await auth.RegisterAsync(request, requestContext.IpAddress, requestContext.UserAgent, cancellationToken);
        if (result.IsSuccess)
        {
            SetRefreshCookie(result.Value!);
        }

        return result.ToActionResult(token => Ok(new { token.AccessToken, token.ExpiresInSeconds, token.ExpiresAt, token.User }));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await auth.LoginAsync(request, requestContext.IpAddress, requestContext.UserAgent, cancellationToken);
        if (result.IsSuccess)
        {
            SetRefreshCookie(result.Value!);
        }

        return result.ToActionResult(token => Ok(new { token.AccessToken, token.ExpiresInSeconds, token.ExpiresAt, token.User }));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var raw = ReadRefreshToken();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "No refresh token was supplied.");
        }

        var result = await auth.RefreshAsync(raw, requestContext.IpAddress, requestContext.UserAgent, cancellationToken);
        if (result.IsSuccess)
        {
            SetRefreshCookie(result.Value!);
        }

        return result.ToActionResult(token => Ok(new { token.AccessToken, token.ExpiresInSeconds, token.ExpiresAt, token.User }));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? request, CancellationToken cancellationToken)
    {
        var raw = request?.RefreshToken ?? ReadRefreshToken();
        if (!string.IsNullOrWhiteSpace(raw))
        {
            await auth.LogoutAsync(raw, request?.AllDevices ?? false, cancellationToken);
        }

        Response.Cookies.Delete(RefreshCookieName);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await auth.GetCurrentUserAsync(userId, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPatch("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await auth.UpdateProfileAsync(userId, request, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await auth.ChangePasswordAsync(userId, request, cancellationToken);
        return result.ToActionResult(() =>
        {
            Response.Cookies.Delete(RefreshCookieName);
            return new NoContentResult();
        });
    }

    private string? ReadRefreshToken() => Request.Cookies[RefreshCookieName];

    private void SetRefreshCookie(TokenResponse token)
    {
        Response.Cookies.Append(RefreshCookieName, token.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/api/auth",
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.AddDays(30)
        });
    }
}
