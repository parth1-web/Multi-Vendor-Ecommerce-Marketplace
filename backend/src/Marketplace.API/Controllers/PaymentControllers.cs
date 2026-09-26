using System.Text.Json;
using Marketplace.API.Middleware;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Notifications.DTOs;
using Marketplace.Application.Modules.Notifications.Abstractions;
using Marketplace.Application.Modules.Payments.Abstractions;
using Marketplace.Application.Modules.Payments.DTOs;
using Marketplace.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.RateLimiting;

namespace Marketplace.API.Controllers;

/// <summary>
/// Payments. The webhook endpoint is unauthenticated but HMAC-verified and idempotent;
/// nothing else in this controller trusts the browser.
/// </summary>
[ApiController]
[Route("api/payments")]
[Authorize]
public sealed class PaymentsController(IPaymentService payments) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault() ?? request.IdempotencyKey;
        var result = await payments.CreateAsync(request with { IdempotencyKey = key }, cancellationToken);
        return result.ToActionResult(payment => StatusCode(StatusCodes.Status201Created, payment));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        (await payments.GetAsync(id, cancellationToken)).ToActionResult();

    [HttpPost("{id:guid}/verify")]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Verify(Guid id, CancellationToken cancellationToken) =>
        (await payments.VerifyAsync(id, cancellationToken)).ToActionResult();

    [HttpGet]
    [Authorize(Policy = Security.AuthorizationPolicies.AdminOnly)]
    [ProducesResponseType(typeof(PagedResult<PaymentResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? status,
        [FromQuery] Guid? orderId, [FromQuery] string? search, CancellationToken cancellationToken) =>
        Ok(await payments.ListAllAsync(new PaymentListQuery(page, pageSize, ParseStatus(status), orderId, search), cancellationToken));

    /// <summary>
    /// Gateway callback. The raw body is read first because the signature is computed
    /// over it; a duplicate provider event id is acknowledged without re-applying.
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [EnableRateLimiting("webhook")]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return BadRequest(new ProblemDetails { Title = "Empty webhook body." });
        }

        var envelope = ParseWebhook(rawBody);
        var result = await payments.HandleWebhookAsync(envelope, cancellationToken);

        if (result.IsFailure)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Webhook rejected.",
                Detail = result.Error,
                Status = StatusCodes.Status409Conflict
            });
        }

        return Ok(result.Value);
    }

    private WebhookEnvelope ParseWebhook(string rawBody)
    {
        using var document = JsonDocument.Parse(rawBody);

        string? GetString(params string[] names)
        {
            foreach (var name in names)
            {
                if (document.RootElement.TryGetProperty(name, out var value))
                {
                    return value.ValueKind switch
                    {
                        JsonValueKind.String => value.GetString(),
                        JsonValueKind.Number => value.ToString(),
                        _ => null
                    };
                }
            }

            return null;
        }

        decimal? GetDecimal(params string[] names)
        {
            foreach (var name in names)
            {
                if (document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number)
                {
                    return value.GetDecimal();
                }
            }

            return null;
        }

        var signature = Request.Headers["X-Signature"].FirstOrDefault()
                        ?? Request.Headers["X-Hub-Signature-256"].FirstOrDefault();

        return new WebhookEnvelope(
            GetString("provider", "event_source") ?? "Mock",
            GetString("event_id", "transaction_id", "id") ?? Guid.NewGuid().ToString("N"),
            signature,
            GetString("event_type", "status", "type") ?? "completed",
            rawBody,
            GetString("gateway_payment_id", "token", "reference"),
            GetDecimal("amount", "total_amount"),
            GetString("currency"),
            GetString("order_number", "orderNumber"));
    }

    private static PaymentStatus? ParseStatus(string? status) =>
        Enum.TryParse<PaymentStatus>(status, ignoreCase: true, out var parsed) ? parsed : null;
}

/// <summary>Refund requests and admin review.</summary>
[ApiController]
[Route("api/refunds")]
[Authorize]
public sealed class RefundsController(IRefundService refunds) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(RefundResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> RequestRefund([FromBody] CreateRefundRequest request, CancellationToken cancellationToken) =>
        (await refunds.RequestAsync(request, cancellationToken)).ToActionResult(refund => StatusCode(StatusCodes.Status201Created, refund));

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<RefundResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListOwn(CancellationToken cancellationToken) => Ok(await refunds.ListOwnAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(RefundResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        (await refunds.GetAsync(id, cancellationToken)).ToActionResult();

    [HttpGet("all")]
    [Authorize(Policy = Security.AuthorizationPolicies.AdminOnly)]
    [ProducesResponseType(typeof(PagedResult<RefundResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAll(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? status,
        [FromQuery] Guid? orderId, [FromQuery] string? search, CancellationToken cancellationToken) =>
        Ok(await refunds.ListAllAsync(
            new RefundListQuery(page, pageSize,
                Enum.TryParse<RefundStatus>(status, true, out var parsed) ? parsed : null, orderId, search),
            cancellationToken));

    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = Security.AuthorizationPolicies.AdminOnly)]
    [ProducesResponseType(typeof(RefundResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Review(Guid id, [FromBody] ReviewRefundRequest request, CancellationToken cancellationToken) =>
        (await refunds.ReviewAsync(id, request, cancellationToken)).ToActionResult();
}

/// <summary>Notifications for the signed-in user.</summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController(INotificationQueryService notifications) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<NotificationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] bool? unreadOnly,
        [FromQuery] string? type, CancellationToken cancellationToken) =>
        Ok(await notifications.ListAsync(page, pageSize, unreadOnly,
            Enum.TryParse<NotificationType>(type, true, out var parsed) ? parsed : null, cancellationToken));

    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(NotificationUnreadCountResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnreadCount(CancellationToken cancellationToken) =>
        Ok(await notifications.GetUnreadCountAsync(cancellationToken));

    [HttpPut("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken) =>
        (await notifications.MarkReadAsync(id, cancellationToken)).ToActionResult();

    [HttpPut("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken) =>
        (await notifications.MarkAllReadAsync(cancellationToken)).ToActionResult();

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await notifications.DeleteAsync(id, cancellationToken)).ToActionResult();
}
