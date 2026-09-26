namespace Marketplace.Domain.Common;

/// <summary>
/// Monetary amount with currency. Value object — arithmetic is explicit and always
/// rounds to two decimal places (banker's-rounding free, away-from-zero) so totals are
/// reproducible across platforms.
/// </summary>
public readonly struct Money : IEquatable<Money>
{
    public Money(decimal amount, string currency)
    {
        if (amount != decimal.Round(amount, 2, MidpointRounding.AwayFromZero))
        {
            amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        }

        Amount = amount;
        Currency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.ToUpperInvariant();
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public bool IsZero => Amount == 0m;

    public bool IsPositive => Amount > 0m;

    public bool IsNegative => Amount < 0m;

    public static Money Zero(string currency = "USD") => new(0m, currency);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    public Money Multiply(int factor) => new(Amount * factor, Currency);

    /// <summary>Applies a percentage (e.g. <c>10</c> for 10%).</summary>
    public Money Percentage(decimal percentage) => new(Amount * percentage / 100m, Currency);

    public Money WithAmount(decimal amount) => new(amount, Currency);

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new ValidationException(nameof(other), $"Currency mismatch: {Currency} vs {other.Currency}.");
        }
    }

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static Money operator *(Money money, int factor) => money.Multiply(factor);

    public static bool operator >(Money left, Money right) => left.Amount > right.Amount;

    public static bool operator <(Money left, Money right) => left.Amount < right.Amount;

    public static bool operator >=(Money left, Money right) => left.Amount >= right.Amount;

    public static bool operator <=(Money left, Money right) => left.Amount <= right.Amount;

    public bool Equals(Money other) => Amount == other.Amount && string.Equals(Currency, other.Currency, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Money other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Amount, Currency);

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
