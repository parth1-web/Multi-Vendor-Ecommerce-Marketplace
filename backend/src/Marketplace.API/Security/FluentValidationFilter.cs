using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Marketplace.API.Security;

/// <summary>
/// Runs every FluentValidation validator registered for the action's arguments and turns
/// failures into a validation problem-details response. Registering validators with
/// <c>AddValidatorsFromAssembly</c> only adds them to the container; without this filter
/// nothing would ever execute them, and malformed requests would reach the services.
/// </summary>
public sealed class FluentValidationFilter(IServiceProvider services, ILogger<FluentValidationFilter> logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var failures = new List<ValidationFailure>();

        foreach (var argument in context.ActionArguments)
        {
            if (argument.Value is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.Value.GetType());
            if (services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var validationContext = new ValidationContext<object>(argument.Value);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted).ConfigureAwait(false);
            failures.AddRange(result.Errors);
        }

        if (failures.Count == 0)
        {
            await next().ConfigureAwait(false);
            return;
        }

        logger.LogWarning(
            "Request {Method} {Path} rejected: {Errors} validation failure(s).",
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path,
            failures.Count);

        var errors = failures
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray(),
                StringComparer.Ordinal);

        var payload = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Instance = context.HttpContext.Request.Path
        };

        payload.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

        context.Result = new BadRequestObjectResult(payload)
        {
            ContentTypes = { "application/problem+json" }
        };
    }
}

/// <summary>Shared serialiser settings so validation output matches the rest of the API.</summary>
internal static class ValidationJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
