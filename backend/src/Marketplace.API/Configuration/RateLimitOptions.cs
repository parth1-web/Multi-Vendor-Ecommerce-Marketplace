using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Marketplace.API.Configuration;

/// <summary>Names of the rate limiter policies, shared by the pipeline and the attributes.</summary>
public static class RateLimitPolicies
{
    /// <summary>Applies to every endpoint that does not opt into a narrower policy.</summary>
    public const string Global = "global";

    /// <summary>Login, register, refresh and password endpoints.</summary>
    public const string Auth = "auth";

    /// <summary>Cart and order placement endpoints.</summary>
    public const string Checkout = "checkout";

    /// <summary>Payment provider webhooks.</summary>
    public const string Webhook = "webhook";
}

/// <summary>
/// Builds a fixed-window partition per caller, reading the permit count from the resolved
/// options so configuration is honoured no matter when it was added.
/// </summary>
internal static class RateLimitPartitionFactory
{
    public static RateLimitPartition<string> FixedWindow(
        HttpContext context,
        Func<RateLimitOptions, int> permitSelector)
    {
        var limits = context.RequestServices
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<RateLimitOptions>>().Value;

        var caller = context.User.Identity?.Name
                     ?? context.Connection.RemoteIpAddress?.ToString()
                     ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(
            caller,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitSelector(limits),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    }
}

/// <summary>
/// Permit counts for the fixed-window rate limiters. They are configuration rather than
/// constants so a deployment can tighten or relax them, and so a test host can raise the
/// limits instead of throttling its own suite.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Requests per minute across the whole API for one caller.</summary>
    public int GlobalPermitLimit { get; set; } = 3000;

    /// <summary>Requests per minute on login, register, refresh and password endpoints.</summary>
    public int AuthPermitLimit { get; set; } = 20;

    /// <summary>Requests per minute on cart and order placement endpoints.</summary>
    public int CheckoutPermitLimit { get; set; } = 40;

    /// <summary>Requests per minute on payment webhooks.</summary>
    public int WebhookPermitLimit { get; set; } = 600;
}
