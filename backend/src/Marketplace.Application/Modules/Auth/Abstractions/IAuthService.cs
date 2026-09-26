using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Auth.DTOs;

namespace Marketplace.Application.Modules.Auth.Abstractions;

/// <summary>Authentication use cases. Implemented by <c>AuthService</c>.</summary>
public interface IAuthService
{
    Task<Result<TokenResponse>> RegisterAsync(RegisterRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<Result<TokenResponse>> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<Result<TokenResponse>> RefreshAsync(string rawRefreshToken, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<Result> LogoutAsync(string rawRefreshToken, bool allDevices, CancellationToken cancellationToken = default);

    Task<Result<UserResponse>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result<UserResponse>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default);

    Task<Result> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);
}
