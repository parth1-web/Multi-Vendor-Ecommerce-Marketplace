using System.Diagnostics.CodeAnalysis;

namespace Marketplace.Domain.Common;

/// <summary>
/// Thrown when a domain invariant is violated. Surfaces to clients as HTTP 400 or 422
/// depending on the concrete subtype, never as a 500.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A value supplied to a domain operation failed validation.</summary>
public sealed class ValidationException : DomainException
{
    public ValidationException(string message)
        : base(message)
    {
    }

    public ValidationException(string field, string message)
        : base(message)
    {
        Field = field;
    }

    /// <summary>Optional field path the failure should be attached to.</summary>
    public string? Field { get; }
}

/// <summary>An aggregate was asked to move into a state its rules forbid.</summary>
public sealed class InvalidStateTransitionException : DomainException
{
    public InvalidStateTransitionException(string entity, string from, string to)
        : base($"{entity} cannot move from '{from}' to '{to}'.")
    {
        Entity = entity;
        From = from;
        To = to;
    }

    public string Entity { get; }

    public string From { get; }

    public string To { get; }
}

/// <summary>The requested stock is not available. Surfaces as HTTP 409.</summary>
public sealed class InsufficientStockException : DomainException
{
    public InsufficientStockException(string productName, int requested, int available)
        : base($"Only {available} unit(s) of '{productName}' are available, {requested} requested.")
    {
        ProductName = productName;
        Requested = requested;
        Available = available;
    }

    public string ProductName { get; }

    public int Requested { get; }

    public int Available { get; }
}

/// <summary>A uniqueness rule on a natural key was violated. Surfaces as HTTP 409.</summary>
public sealed class DuplicateEntityException : DomainException
{
    public DuplicateEntityException(string entity, string field, string value)
        : base($"{entity} with {field} '{value}' already exists.")
    {
        Entity = entity;
        Field = field;
        Value = value;
    }

    public string Entity { get; }

    public string Field { get; }

    public string Value { get; }
}

/// <summary>An operation was attempted on a resource the caller does not own. Surfaces as HTTP 403.</summary>
public sealed class OwnershipException : DomainException
{
    public OwnershipException(string message)
        : base(message)
    {
    }

    public OwnershipException(string resource, string reason)
        : base($"Access to {resource} is denied: {reason}")
    {
    }
}

/// <summary>A business rule rejected an otherwise well-formed request. Surfaces as HTTP 422.</summary>
public sealed class BusinessRuleException : DomainException
{
    public BusinessRuleException(string message)
        : base(message)
    {
    }
}

/// <summary>Helper for guarding arguments inside domain code.</summary>
public static class Guard
{
    public static void NotNull([NotNull] object? value, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value is null)
        {
            throw new ValidationException(name ?? "value", "is required.");
        }
    }

    public static void NotNullOrWhiteSpace([NotNull] string? value, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(name ?? "value", "is required and cannot be empty.");
        }
    }

    public static void NotEmpty(Guid value, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value == Guid.Empty)
        {
            throw new ValidationException(name ?? "value", "must be a real identifier.");
        }
    }

    public static void GreaterThanZero(int value, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value <= 0)
        {
            throw new ValidationException(name ?? "value", "must be greater than zero.");
        }
    }

    public static void GreaterThanOrEqualToZero(decimal value, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value < 0m)
        {
            throw new ValidationException(name ?? "value", "cannot be negative.");
        }
    }

    public static void InRange(decimal value, decimal min, decimal max, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value < min || value > max)
        {
            throw new ValidationException(name ?? "value", $"must be between {min} and {max}.");
        }
    }
}
