using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Auth.Abstractions;
using Marketplace.Application.Modules.Auth.DTOs;
using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Events;
using Marketplace.Domain.Identity;
using Marketplace.Domain.Sellers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marketplace.Application.Modules.Auth.Services;

/// <summary>
/// Registration, login, refresh-token rotation and profile management.
///
/// Refresh tokens are single-use: every refresh revokes the presented token and issues a
/// successor. Presenting an already-rotated token is treated as theft and revokes the
/// entire token family.
/// </summary>
public sealed class AuthService(
    IRepository<User> users,
    IRepository<RefreshToken> refreshTokens,
    IRepository<Seller> sellers,
    IRepository<SellerStore> stores,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IRefreshTokenProtector tokenProtector,
    IUnitOfWork unitOfWork,
    IClock clock,
    IAuditService auditService,
    INotificationService notificationService,
    IOptions<JwtOptions> jwtOptions,
    ILogger<AuthService> logger) : IAuthService
{
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;

    public async Task<Result<TokenResponse>> RegisterAsync(RegisterRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await users.AnyAsync(u => u.Email == email, cancellationToken).ConfigureAwait(false))
        {
            return Result<TokenResponse>.Failure(new Dictionary<string, string[]>
            {
                [nameof(request.Email)] = ["An account with this email already exists."]
            });
        }

        var now = clock.UtcNow;
        var user = User.Register(
            email,
            passwordHasher.Hash(request.Password),
            request.FirstName,
            request.LastName,
            request.PhoneNumber,
            request.Role,
            now);

        await users.AddAsync(user, cancellationToken).ConfigureAwait(false);

        Guid? sellerId = null;
        if (request.Role == UserRole.Seller)
        {
            // A seller account starts as a pending application; nothing can be listed until
            // an admin approves it.
            var seller = Seller.Apply(
                user.Id,
                request.FirstName + " " + request.LastName,
                null,
                request.PhoneNumber,
                null,
                null,
                null,
                null,
                10m,
                now);

            sellerId = seller.Id;
            await sellers.AddAsync(seller, cancellationToken).ConfigureAwait(false);

            var store = SellerStore.Create(
                seller.Id,
                $"{request.FirstName}'s Store",
                Slug.Create($"{request.FirstName}-store"),
                "Store pending approval.",
                now);

            await stores.AddAsync(store, cancellationToken).ConfigureAwait(false);
        }

        user.AddDomainEvent(new UserRegisteredEvent(user.Id, user.Email, user.Role, now));

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.Register, nameof(User), user.Id, user.Email,
            new { Email = user.Email, Role = user.Role.ToString() }, cancellationToken).ConfigureAwait(false);

        if (user.Role == UserRole.Seller)
        {
            await notificationService.NotifyAsync(
                user.Id,
                NotificationType.SellerApproved,
                "Seller application received",
                "We are reviewing your seller application. You will be notified as soon as it is approved.",
                "/seller",
                NotificationAudience.Seller,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await notificationService.NotifyAsync(
                user.Id,
                NotificationType.SystemNotification,
                "Welcome to the marketplace",
                "Your account is ready. Start browsing thousands of products from independent sellers.",
                "/products",
                NotificationAudience.Customer,
                cancellationToken).ConfigureAwait(false);
        }

        return await IssueTokensAsync(user, Guid.Empty, ipAddress, userAgent, RefreshTokenProtector.CreateRawToken(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<TokenResponse>> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await users.Query().FirstOrDefaultAsync(u => u.Email == email, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            // Identical response for unknown email and wrong password: no user enumeration.
            await auditService.RecordAsync(AuditAction.LoginFailed, nameof(User), null, email, null, cancellationToken).ConfigureAwait(false);
            return Result<TokenResponse>.Failure("Invalid email or password.");
        }

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            logger.LogWarning("Failed login attempt for {Email}", email);
            await auditService.RecordAsync(AuditAction.LoginFailed, nameof(User), user.Id, email, null, cancellationToken).ConfigureAwait(false);
            return Result<TokenResponse>.Failure("Invalid email or password.");
        }

        if (!user.IsActive)
        {
            return Result<TokenResponse>.Failure("This account has been deactivated.");
        }

        user.RecordLogin(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.Login, nameof(User), user.Id, email, null, cancellationToken).ConfigureAwait(false);

        return await IssueTokensAsync(user, Guid.Empty, ipAddress, userAgent, RefreshTokenProtector.CreateRawToken(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<TokenResponse>> RefreshAsync(string rawRefreshToken, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var hash = tokenProtector.Protect(rawRefreshToken);
        var token = await refreshTokens.Query().FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken).ConfigureAwait(false);

        if (token is null)
        {
            return Result<TokenResponse>.Failure("The refresh token is not valid.");
        }

        var now = clock.UtcNow;

        if (token.IsRevoked)
        {
            // Reuse of a rotated token: assume the family is compromised and revoke it all.
            logger.LogWarning("Refresh token reuse detected for user {UserId}, family {FamilyId}", token.UserId, token.FamilyId);
            await RevokeFamilyAsync(token.FamilyId, now, "reuse-detected", cancellationToken).ConfigureAwait(false);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await auditService.RecordAsync(AuditAction.LoginFailed, nameof(RefreshToken), token.Id, token.UserId.ToString(),
                new { Reason = "reuse-detected" }, cancellationToken).ConfigureAwait(false);
            return Result<TokenResponse>.Failure(
                "The refresh token is no longer valid. Please sign in again.",
                ResultErrorCodes.Conflict);
        }

        if (token.IsExpired(now))
        {
            return Result<TokenResponse>.Failure("The refresh token has expired. Please sign in again.");
        }

        var user = await users.GetByIdAsync(token.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result<TokenResponse>.Failure("This account is no longer available.");
        }

        // Single-use rotation: the presented token is revoked and a brand-new raw token is
        // minted whose digest is the only thing persisted.
        var rawSuccessor = RefreshTokenProtector.CreateRawToken();
        var successor = token.RotateTo(tokenProtector.Protect(rawSuccessor), now);
        await refreshTokens.AddAsync(successor, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await IssueRotatedTokensAsync(user, rawSuccessor, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result> LogoutAsync(string rawRefreshToken, bool allDevices, CancellationToken cancellationToken = default)
    {
        var hash = tokenProtector.Protect(rawRefreshToken);
        var token = await refreshTokens.Query().FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken).ConfigureAwait(false);

        if (token is null)
        {
            return Result.Failure("The refresh token is not valid.");
        }

        var now = clock.UtcNow;
        if (allDevices)
        {
            await RevokeFamilyAsync(token.FamilyId, now, "logout-all-devices", cancellationToken).ConfigureAwait(false);
        }
        else
        {
            token.Revoke(now, "logout");
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.Logout, nameof(RefreshToken), token.Id, token.UserId.ToString(),
            new { AllDevices = allDevices }, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    public async Task<Result<UserResponse>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Result<UserResponse>.Failure("User not found.", ResultErrorCodes.NotFound);
        }

        var dto = await MapAsync(user, cancellationToken).ConfigureAwait(false);
        return Result<UserResponse>.Success(dto);
    }

    public async Task<Result<UserResponse>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Result<UserResponse>.Failure("User not found.", ResultErrorCodes.NotFound);
        }

        user.UpdateProfile(request.FirstName, request.LastName, request.PhoneNumber, request.AvatarUrl, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.Register, nameof(User), user.Id, user.Email, null, cancellationToken).ConfigureAwait(false);

        return Result<UserResponse>.Success(await MapAsync(user, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Result.Failure("User not found.", ResultErrorCodes.NotFound);
        }

        if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return Result.Failure("The current password is incorrect.", new Dictionary<string, string[]>
            {
                [nameof(request.CurrentPassword)] = ["The current password is incorrect."]
            });
        }

        user.ChangePassword(passwordHasher.Hash(request.NewPassword), clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Changing a password invalidates every existing session.
        var now = clock.UtcNow;
        var active = await refreshTokens.Query().Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var token in active)
        {
            token.Revoke(now, "password-changed");
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.PasswordChanged, nameof(User), user.Id, user.Email, null, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    private async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, string reason, CancellationToken cancellationToken)
    {
        var family = await refreshTokens.Query()
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var token in family)
        {
            token.Revoke(now, reason);
        }
    }

    private async Task<Result<TokenResponse>> IssueTokensAsync(
        User user,
        Guid familyId,
        string? ipAddress,
        string? userAgent,
        string rawRefreshToken,
        CancellationToken cancellationToken)
    {
        var dto = await MapAsync(user, cancellationToken).ConfigureAwait(false);
        var (accessToken, expiresAt) = tokenService.CreateAccessToken(dto);

        var token = RefreshToken.Create(
            user.Id,
            tokenProtector.Protect(rawRefreshToken),
            familyId,
            clock.UtcNow,
            _jwtOptions.RefreshTokenDays,
            ipAddress,
            userAgent);

        await refreshTokens.AddAsync(token, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var seconds = (int)Math.Max(0, (expiresAt - clock.UtcNow).TotalSeconds);

        return Result<TokenResponse>.Success(new TokenResponse(accessToken, rawRefreshToken, seconds, expiresAt, dto));
    }

    /// <summary>
    /// Builds the response for an already-rotated token: the successor entity is persisted by
    /// the caller, so only the access token is minted here.
    /// </summary>
    private async Task<Result<TokenResponse>> IssueRotatedTokensAsync(
        User user,
        string rawRefreshToken,
        CancellationToken cancellationToken)
    {
        var dto = await MapAsync(user, cancellationToken).ConfigureAwait(false);
        var (accessToken, expiresAt) = tokenService.CreateAccessToken(dto);
        var seconds = (int)Math.Max(0, (expiresAt - clock.UtcNow).TotalSeconds);

        return Result<TokenResponse>.Success(new TokenResponse(accessToken, rawRefreshToken, seconds, expiresAt, dto));
    }

    private async Task<UserResponse> MapAsync(User user, CancellationToken cancellationToken)
    {
        string? sellerStatus = null;
        string? storeName = null;
        string? storeSlug = null;

        var seller = await sellers.Query().AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == user.Id, cancellationToken).ConfigureAwait(false);

        if (seller is not null)
        {
            sellerStatus = seller.Status.ToString();

            var store = await stores.Query()
                .FirstOrDefaultAsync(s => s.SellerId == seller.Id, cancellationToken)
                .ConfigureAwait(false);
            storeName = store?.Name;
            storeSlug = store?.SlugValue;
        }

        return new UserResponse(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.FullName,
            user.PhoneNumber,
            user.AvatarUrl,
            user.Role,
            user.IsEmailConfirmed,
            user.IsActive,
            user.CreatedAt,
            user.LastLoginAt,
            seller?.Id,
            sellerStatus,
            storeName,
            storeSlug);
    }
}
