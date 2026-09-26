using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Modules.Auth.Abstractions;
using Marketplace.Application.Modules.Auth.DTOs;
using Marketplace.Domain.Enums;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Marketplace.Application.Modules.Auth.Services;

/// <summary>
/// Issues HS256 JWTs. The signing key comes from configuration only — never from a
/// request, never from a hard-coded fallback in production.
/// </summary>
public sealed class TokenService(IOptions<JwtOptions> options, IClock clock) : ITokenService
{
    /// <summary>
    /// Identifier written into the token's <c>kid</c> header. The validator matches keys by
    /// this value, and IdentityModel refuses a key whose id is empty when the token has no
    /// <c>kid</c>, so signing and validating must publish the same id.
    /// </summary>
    public const string SigningKeyId = "mp-hs256-v1";

    /// <summary>Claim carrying the seller id, so seller scope is derived from the token.</summary>
    public const string SellerClaim = "sid";

    /// <summary>Claim carrying the role name.</summary>
    public const string RoleClaim = "role";

    private readonly JwtOptions _options = options.Value;
    private readonly IClock _clock = clock;

    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(UserResponse user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrWhiteSpace(_options.Key) || _options.Key.Length < 32)
        {
            throw new InvalidOperationException("Jwt:Key must be configured with at least 32 characters.");
        }

        // Issued and expiry claims must come from the application clock: the rest of the
        // session (refresh tokens, cookie expiry, DTO payloads) is measured with it, and a
        // token stamped with a different source would disagree with its own response.
        var now = _clock.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(RoleClaim, user.Role.ToString()),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Exp, expires.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        if (user.SellerId is not null)
        {
            claims.Add(new Claim(SellerClaim, user.SellerId.Value.ToString()));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)) { KeyId = SigningKeyId };
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public AccessTokenClaims? ReadClaims(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token))
        {
            return null;
        }

        JwtSecurityToken jwt;
        try
        {
            jwt = handler.ReadJwtToken(token);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var subject = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
        if (!Guid.TryParse(subject, out var userId))
        {
            return null;
        }

        var roleValue = jwt.Claims.FirstOrDefault(c => c.Type == RoleClaim)?.Value;
        var role = Enum.TryParse<UserRole>(roleValue, ignoreCase: true, out var parsed) ? parsed : UserRole.Customer;

        var sellerValue = jwt.Claims.FirstOrDefault(c => c.Type == SellerClaim)?.Value;
        Guid? sellerId = Guid.TryParse(sellerValue, out var sid) ? sid : null;

        var jti = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;

        return new AccessTokenClaims(
            userId,
            jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Email)?.Value ?? string.Empty,
            role,
            sellerId,
            jti ?? Guid.NewGuid().ToString("N"));
    }
}

/// <summary>Generates cryptographically random refresh tokens and stores only their digest.</summary>
public sealed class RefreshTokenProtector : IRefreshTokenProtector
{
    private const int TokenByteLength = 64;

    /// <summary>Creates a new opaque token. The raw value is returned to the caller only once.</summary>
    public static string CreateRawToken() =>
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(TokenByteLength));

    public string Protect(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes);
    }

    public bool Matches(string rawToken, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        // A malformed stored digest must be treated as a mismatch, never as an exception.
        if (storedHash.Length != 64 || !IsHex(storedHash))
        {
            return false;
        }

        var computed = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        var expected = Convert.FromHexString(storedHash);
        return CryptographicOperations.FixedTimeEquals(computed, expected);
    }

    private static bool IsHex(string value)
    {
        foreach (var c in value)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// PBKDF2-HMAC-SHA256 password hashing with a per-password salt. 100 000 iterations is
/// the current OWASP guidance for SHA-256 and is configurable through the hasher options.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int DefaultIterations = 100_000;
    private const string Prefix = "pbkdf2-sha256";

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var iterations = ResolveIterations();
        var subkey = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashBytes);

        return string.Join('$', Prefix, iterations, Convert.ToBase64String(salt), Convert.ToBase64String(subkey));
    }

    public bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(passwordHash))
        {
            return false;
        }

        var parts = passwordHash.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) || iterations < 1)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public bool NeedsRehash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return true;
        }

        var parts = passwordHash.Split('$');
        return parts.Length != 4 || !int.TryParse(parts[1], out var iterations) || iterations < ResolveIterations();
    }

    private static int ResolveIterations() => DefaultIterations;
}
