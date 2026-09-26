using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Modules.Auth.DTOs;
using Marketplace.Domain.Enums;

namespace Marketplace.Application.Modules.Auth.Abstractions;

/// <summary>Issues and validates access tokens.</summary>
public interface ITokenService
{
    /// <summary>Creates a signed access token carrying the user id, role and seller id.</summary>
    (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(UserResponse user);

    /// <summary>Extracts the principal described by a token without validating the signature.</summary>
    AccessTokenClaims? ReadClaims(string token);
}

/// <summary>Claims carried by an access token.</summary>
public sealed record AccessTokenClaims(Guid UserId, string Email, UserRole Role, Guid? SellerId, string TokenId);

/// <summary>JWT configuration, bound from the <c>Jwt</c> configuration section.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = string.Empty;

    public string Issuer { get; set; } = "marketplace-api";

    public string Audience { get; set; } = "marketplace-web";

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 30;

    public int ClockSkewSeconds { get; set; } = 30;
}

/// <summary>Configuration for the distributed cache.</summary>
public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public bool Enabled { get; set; }

    public string ConnectionString { get; set; } = "localhost:6379";

    public string InstancePrefix { get; set; } = "marketplace:";

    public int DefaultTtlMinutes { get; set; } = 10;
}

/// <summary>Marketplace-wide business and presentation settings.</summary>
public sealed class MarketplaceOptions
{
    public const string SectionName = "Marketplace";

    public decimal CommissionRate { get; set; } = 10m;

    public string Currency { get; set; } = "USD";

    public decimal TaxRate { get; set; }

    public decimal FreeShippingThreshold { get; set; } = 150m;

    public decimal StandardShippingCost { get; set; } = 9.99m;

    public int ReservationMinutes { get; set; } = 15;

    public int LowStockThreshold { get; set; } = 5;

    public int AbandonedCartDays { get; set; } = 30;

    public string FrontendBaseUrl { get; set; } = "http://localhost:3000";

    public string SupportEmail { get; set; } = "support@marketplace.dev";
}

/// <summary>Payment provider configuration.</summary>
public sealed class PaymentOptions
{
    public const string SectionName = "Payment";

    public PaymentProvider DefaultProvider { get; set; } = PaymentProvider.Mock;

    public string WebhookSecret { get; set; } = string.Empty;

    public KhaltiOptions Khalti { get; set; } = new();

    public EsewaOptions Esewa { get; set; } = new();

    public StripeOptions Stripe { get; set; } = new();
}

public sealed class KhaltiOptions
{
    public string PublicKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://dev.khalti.com/api";
}

public sealed class EsewaOptions
{
    public string MerchantId { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://uat.esewa.com.np";
}

public sealed class StripeOptions
{
    public string SecretKey { get; set; } = string.Empty;

    public string WebhookSecret { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.stripe.com";
}

/// <summary>CORS allow-list configuration.</summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];
}

/// <summary>Resolves the configured <see cref="IPaymentGateway"/> for a provider.</summary>
public interface IPaymentGatewayResolver
{
    IPaymentGateway Resolve(PaymentProvider provider);

    IReadOnlyCollection<string> AvailableProviders { get; }
}

/// <summary>Hashes raw refresh tokens so only digests are persisted.</summary>
public interface IRefreshTokenProtector
{
    string Protect(string rawToken);

    bool Matches(string rawToken, string storedHash);
}
