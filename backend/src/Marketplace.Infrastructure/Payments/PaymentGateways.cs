using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Modules.Auth.Abstractions;
using Marketplace.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marketplace.Infrastructure.Payments;

/// <summary>Resolves the configured gateway for a provider.</summary>
public sealed class PaymentGatewayResolver : IPaymentGatewayResolver
{
    private readonly Dictionary<PaymentProvider, IPaymentGateway> _gateways;

    public PaymentGatewayResolver(IEnumerable<IPaymentGateway> gateways, IOptions<PaymentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(gateways);

        var defaultProvider = options.Value.DefaultProvider;
        _gateways = gateways.GroupBy(g => Parse(g.Name)).ToDictionary(g => g.Key, g => g.First());

        if (!_gateways.ContainsKey(defaultProvider))
        {
            _gateways[defaultProvider] = gateways.First(g => g.Name == nameof(MockPaymentGateway));
        }
    }

    public IReadOnlyCollection<string> AvailableProviders => _gateways.Keys.Select(k => k.ToString()).ToList();

    public IPaymentGateway Resolve(PaymentProvider provider) =>
        _gateways.TryGetValue(provider, out var gateway) ? gateway : _gateways[PaymentProvider.Mock];

    private static PaymentProvider Parse(string name) =>
        Enum.TryParse<PaymentProvider>(name, ignoreCase: true, out var provider) ? provider : PaymentProvider.Mock;
}

/// <summary>
/// Shared plumbing for real gateways: JSON posting, timeout handling and a single place
/// where the webhook signature is verified.
/// </summary>
public abstract class HttpPaymentGateway : IPaymentGateway
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected HttpPaymentGateway(HttpClient httpClient, ILogger logger, string name)
    {
        HttpClient = httpClient;
        Logger = logger;
        Name = name;
    }

    public string Name { get; }

    public abstract bool RequiresCustomerAction { get; }

    protected ILogger Logger { get; }

    protected HttpClient HttpClient { get; }

    public abstract Task<PaymentInitiationResult> CreatePaymentAsync(PaymentInitiationRequest request, CancellationToken cancellationToken = default);

    public abstract Task<PaymentVerificationResult> VerifyAsync(PaymentVerificationRequest request, CancellationToken cancellationToken = default);

    public virtual Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RefundResult(false, null, $"{Name} refunds are not enabled in this deployment.", null));

    public virtual bool VerifySignature(string rawBody, string? signature, string secret) =>
        WebhookSignature.Verify(rawBody, signature, secret);

    protected async Task<TResponse?> PostJsonAsync<TRequest, TResponse>(string url, TRequest payload, HttpClient client, CancellationToken cancellationToken)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            Logger.LogWarning("{Gateway} returned {Status}: {Body}", Name, (int)response.StatusCode, body);
            return default;
        }

        return JsonSerializer.Deserialize<TResponse>(body, Json);
    }
}

/// <summary>Khalti wallet gateway.</summary>
public sealed class KhaltiPaymentGateway(HttpClient httpClient, IOptions<PaymentOptions> options, ILogger<KhaltiPaymentGateway> logger)
    : HttpPaymentGateway(httpClient, logger, "Khalti")
{
    private readonly KhaltiOptions _options = options.Value.Khalti;

    public override bool RequiresCustomerAction => true;

    public override async Task<PaymentInitiationResult> CreatePaymentAsync(PaymentInitiationRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            amount = request.Amount,
            currency = request.Currency,
            description = $"Order {request.OrderNumber}",
            return_url = request.SuccessUrl,
            cancel_url = request.CancelUrl,
            callback_url = request.CallbackUrl,
            metadata = new { orderId = request.OrderId.ToString(), paymentId = request.PaymentId.ToString() }
        };

        var response = await PostJsonAsync<object, KhaltiInitiationResponse>($"{_options.BaseUrl}/v2/payment/initiate", payload, HttpClient, cancellationToken).ConfigureAwait(false);

        if (response is null || string.IsNullOrEmpty(response.Token))
        {
            return new PaymentInitiationResult(false, null, null, null, "Khalti did not return an initiation token.", null);
        }

        return new PaymentInitiationResult(
            true,
            response.Token,
            $"https://khalti.com/checkout/{response.Token}",
            null,
            null,
            response.Raw);
    }

    public override async Task<PaymentVerificationResult> VerifyAsync(PaymentVerificationRequest request, CancellationToken cancellationToken = default)
    {
        var response = await PostJsonAsync<object, KhaltiVerificationResponse>(
            $"{_options.BaseUrl}/v2/payment/verify", new { token = request.GatewayPaymentId }, HttpClient, cancellationToken).ConfigureAwait(false);

        if (response is null)
        {
            return new PaymentVerificationResult(false, null, request.Currency, request.GatewayPaymentId, "Khalti verification failed.", null);
        }

        var matches = response.Status == "Completed" &&
                      decimal.Round(response.Amount, 2) == decimal.Round(request.ExpectedAmount, 2);

        return new PaymentVerificationResult(
            matches,
            response.Amount,
            response.Currency ?? request.Currency,
            response.GatewayPaymentId ?? request.GatewayPaymentId,
            matches ? null : "Khalti reported a mismatch or an incomplete payment.",
            response.Raw);
    }

    private sealed class KhaltiInitiationResponse
    {
        public string? Token { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string? Raw => Token is null ? null : $"{{\"token\":\"{Token}\"}}";
    }

    private sealed class KhaltiVerificationResponse
    {
        public string? Status { get; set; }

        public decimal Amount { get; set; }

        public string? Currency { get; set; }

        public string? GatewayPaymentId { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string? Raw => $"{{\"status\":\"{Status}\",\"amount\":{Amount}}}";
    }
}

/// <summary>eSewa gateway.</summary>
public sealed class EsewaPaymentGateway(HttpClient httpClient, IOptions<PaymentOptions> options, ILogger<EsewaPaymentGateway> logger)
    : HttpPaymentGateway(httpClient, logger, "ESewa")
{
    private readonly EsewaOptions _options = options.Value.Esewa;

    public override bool RequiresCustomerAction => true;

    public override async Task<PaymentInitiationResult> CreatePaymentAsync(PaymentInitiationRequest request, CancellationToken cancellationToken = default)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["amt"] = request.Amount.ToString("0.00"),
            ["pid"] = request.OrderNumber,
            ["scd"] = "NRB",
            ["su"] = request.SuccessUrl,
            ["fu"] = request.FailUrl,
            ["rd"] = request.CancelUrl
        });

        using var response = await HttpClient.PostAsync($"{_options.BaseUrl}/api/payment/create", form, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return new PaymentInitiationResult(false, null, null, null, "eSewa did not accept the payment request.", body);
        }

        return new PaymentInitiationResult(true, request.OrderNumber, request.SuccessUrl, null, null, body);
    }

    public override async Task<PaymentVerificationResult> VerifyAsync(PaymentVerificationRequest request, CancellationToken cancellationToken = default)
    {
        var url = $"{_options.BaseUrl}/api/payment/verify?amt={request.ExpectedAmount:0.00}&pid={Uri.EscapeDataString(request.GatewayPaymentId ?? string.Empty)}";
        using var response = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var verified = response.IsSuccessStatusCode &&
                       body.Contains("\"Status\":\"Success\"", StringComparison.OrdinalIgnoreCase);

        return new PaymentVerificationResult(
            verified,
            request.ExpectedAmount,
            request.Currency,
            request.GatewayPaymentId,
            verified ? null : "eSewa reported a failed verification.",
            body);
    }
}

/// <summary>Stripe gateway.</summary>
public sealed class StripePaymentGateway(HttpClient httpClient, IOptions<PaymentOptions> options, ILogger<StripePaymentGateway> logger)
    : HttpPaymentGateway(httpClient, logger, "Stripe")
{
    private readonly StripeOptions _options = options.Value.Stripe;

    public override bool RequiresCustomerAction => true;


    public override async Task<PaymentInitiationResult> CreatePaymentAsync(PaymentInitiationRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            amount = (long)(request.Amount * 100),
            currency = request.Currency.ToLowerInvariant(),
            metadata = new { orderNumber = request.OrderNumber, orderId = request.OrderId.ToString() },
            success_url = request.SuccessUrl,
            cancel_url = request.CancelUrl
        };

        var response = await PostJsonAsync<object, StripeSessionResponse>($"{_options.BaseUrl}/v1/checkout/sessions", payload, HttpClient, cancellationToken).ConfigureAwait(false);

        if (response is null || string.IsNullOrEmpty(response.Url))
        {
            return new PaymentInitiationResult(false, null, null, null, "Stripe did not return a checkout session.", null);
        }

        return new PaymentInitiationResult(true, response.Id, response.Url, null, null, response.Id);
    }

    public override async Task<PaymentVerificationResult> VerifyAsync(PaymentVerificationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.GatewayPaymentId))
        {
            return new PaymentVerificationResult(false, null, request.Currency, null, "Missing Stripe session id.", null);
        }

        using var response = await HttpClient.GetAsync($"{_options.BaseUrl}/v1/checkout/sessions/{request.GatewayPaymentId}", cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var paid = response.IsSuccessStatusCode && body.Contains("\"payment_status\":\"paid\"", StringComparison.OrdinalIgnoreCase);
        var amount = paid ? request.ExpectedAmount : (decimal?)null;

        return new PaymentVerificationResult(paid, amount, request.Currency, request.GatewayPaymentId,
            paid ? null : "Stripe reported the session as unpaid.", body);
    }

    private sealed class StripeSessionResponse
    {
        public string? Id { get; set; }

        public string? Url { get; set; }
    }
}
