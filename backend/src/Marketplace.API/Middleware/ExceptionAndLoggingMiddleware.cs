using System.Diagnostics;
using System.Text.Json;
using Marketplace.Application.Common.Models;
using Marketplace.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.API.Middleware;

/// <summary>
/// Converts every unhandled exception into an RFC 7807 problem document. Internal
/// detail is logged with a correlation id and never returned to the client.
/// </summary>
public sealed class GlobalExceptionHandler(
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, type) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed", "validation"),
            OwnershipException => (StatusCodes.Status403Forbidden, "Forbidden", "forbidden"),
            DuplicateEntityException => (StatusCodes.Status409Conflict, "Conflict", "conflict"),
            InsufficientStockException => (StatusCodes.Status409Conflict, "Insufficient stock", "insufficient-stock"),
            InvalidStateTransitionException => (StatusCodes.Status409Conflict, "Invalid state transition", "invalid-transition"),
            BusinessRuleException => (StatusCodes.Status422UnprocessableEntity, "Business rule violated", "business-rule"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Forbidden", "forbidden"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred", "internal")
        };

        var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? httpContext.TraceIdentifier;

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path} (correlation {CorrelationId})",
                httpContext.Request.Method, httpContext.Request.Path, correlationId);
        }
        else
        {
            logger.LogWarning("{Status} on {Method} {Path}: {Message} (correlation {CorrelationId})",
                status, httpContext.Request.Method, httpContext.Request.Path, exception.Message, correlationId);
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://errors.marketplace.dev/{type}",
            Detail = status >= 500 && !environment.IsDevelopment() ? null : exception.Message,
            Instance = httpContext.Request.Path
        };

        problem.Extensions["correlationId"] = correlationId;

        if (exception is ValidationException { Field: { } field })
        {
            problem.Extensions["errors"] = new Dictionary<string, string[]> { [field] = [exception.Message] };
        }

        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken).ConfigureAwait(false);

        return true;
    }
}

/// <summary>
/// Assigns a correlation id to every request and logs method, path, status and duration.
/// </summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                            ?? Guid.NewGuid().ToString("N");

        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers["X-Correlation-Id"] = correlationId;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();

            logger.Log(
                context.Response.StatusCode >= 500 ? LogLevel.Error : LogLevel.Information,
                "{Method} {Path} responded {Status} in {ElapsedMs} ms (correlation {CorrelationId})",
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                correlationId);
        }
    }
}

/// <summary>Adds the security headers documented in docs/SECURITY.md.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

        if (context.Request.IsHttps)
        {
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        }

        return next(context);
    }
}

/// <summary>Maps <see cref="Result{T}"/> and <see cref="Result"/> onto HTTP responses.</summary>
public static class ResultExtensions
{
    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult>? onSuccess = null)
    {
        if (result.IsSuccess)
        {
            return onSuccess is not null
                ? onSuccess(result.Value!)
                : Results.Ok(result.Value);
        }

        if (result.Errors.Count > 0)
        {
            return Results.ValidationProblem(result.Errors.ToDictionary(e => e.Key, e => e.Value));
        }

        return Results.Problem(
            title: "Request failed",
            detail: result.Error,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            type: "https://errors.marketplace.dev/unprocessable");
    }

    public static IResult ToHttpResult(this Result result, Func<IResult>? onSuccess = null)
    {
        if (result.IsSuccess)
        {
            return onSuccess?.Invoke() ?? Results.NoContent();
        }

        if (result.Errors.Count > 0)
        {
            return Results.ValidationProblem(result.Errors.ToDictionary(e => e.Key, e => e.Value));
        }

        return Results.Problem(
            title: "Request failed",
            detail: result.Error,
            statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    /// <summary>Controller-friendly variant returning <see cref="IActionResult"/>.</summary>
    public static IActionResult ToActionResult<T>(this Result<T> result, Func<T, IActionResult>? onSuccess = null)
    {
        if (result.IsSuccess)
        {
            return onSuccess is not null ? onSuccess(result.Value!) : new OkObjectResult(result.Value);
        }

        if (result.Errors.Count > 0)
        {
            return new BadRequestObjectResult(new ValidationProblemDetails(result.Errors.ToDictionary(e => e.Key, e => e.Value))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed"
            });
        }

        return new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "Request failed",
            Detail = result.Error,
            Type = "https://errors.marketplace.dev/unprocessable"
        })
        {
            StatusCode = StatusCodes.Status422UnprocessableEntity
        };
    }

    /// <summary>Controller-friendly variant returning <see cref="IActionResult"/>.</summary>
    public static IActionResult ToActionResult(this Result result, Func<IActionResult>? onSuccess = null)
    {
        if (result.IsSuccess)
        {
            return onSuccess?.Invoke() ?? new NoContentResult();
        }

        if (result.Errors.Count > 0)
        {
            return new BadRequestObjectResult(new ValidationProblemDetails(result.Errors.ToDictionary(e => e.Key, e => e.Value))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed"
            });
        }

        return new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "Request failed",
            Detail = result.Error,
            Type = "https://errors.marketplace.dev/unprocessable"
        })
        {
            StatusCode = StatusCodes.Status422UnprocessableEntity
        };
    }
}
