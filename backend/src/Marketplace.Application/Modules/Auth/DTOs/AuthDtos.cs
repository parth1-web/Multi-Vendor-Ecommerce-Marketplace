using Marketplace.Domain.Enums;

namespace Marketplace.Application.Modules.Auth.DTOs;

public sealed record RegisterRequest(
    string Email,
    string Password,
    string ConfirmPassword,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    UserRole Role = UserRole.Customer);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshTokenRequest(string? RefreshToken);

public sealed record LogoutRequest(string? RefreshToken, bool AllDevices = false);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string Password, string ConfirmPassword);

public sealed record ConfirmEmailRequest(string Token);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword, string ConfirmPassword);

public sealed record UpdateProfileRequest(
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? AvatarUrl);

/// <summary>Issued token pair. The refresh token is delivered via an HttpOnly cookie by the API.</summary>
public sealed record TokenResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    DateTimeOffset ExpiresAt,
    UserResponse User);

public sealed record UserResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    string? PhoneNumber,
    string? AvatarUrl,
    UserRole Role,
    bool IsEmailConfirmed,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    Guid? SellerId,
    string? SellerStatus,
    string? StoreName,
    string? StoreSlug);
