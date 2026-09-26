using System.Text.RegularExpressions;

namespace Marketplace.Domain.Common;

/// <summary>
/// URL-safe, lowercase identifier used for public routes (products, categories, stores).
/// Value object: created through <see cref="Create"/> or <see cref="FromExisting"/> so an
/// invalid slug can never be constructed.
/// </summary>
public sealed class Slug : IEquatable<Slug>
{
    private static readonly Regex Allowed = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex NonAlphanumeric = new("[^a-z0-9]+", RegexOptions.Compiled);
    private static readonly Regex Separator = new("(^-+|-+$)", RegexOptions.Compiled);

    public const int MaxLength = 160;

    private Slug(string value) => Value = value;

    public string Value { get; }

    public static Slug Create(string source)
    {
        Guard.NotNullOrWhiteSpace(source, nameof(source));
        var value = Generate(source);
        if (string.IsNullOrEmpty(value))
        {
            throw new ValidationException(nameof(source), "must contain at least one letter or digit.");
        }

        return new Slug(value);
    }

    public static Slug FromExisting(string value)
    {
        Guard.NotNullOrWhiteSpace(value, nameof(value));
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > MaxLength)
        {
            normalized = normalized[..MaxLength].Trim('-');
        }

        if (!Allowed.IsMatch(normalized))
        {
            throw new ValidationException(nameof(value), $"'{value}' is not a valid slug.");
        }

        return new Slug(normalized);
    }

    public static string Generate(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var lowered = source.Normalize(System.Text.NormalizationForm.FormD).ToLowerInvariant();
        var ascii = new string(lowered.Where(ch => ch < 128).ToArray());
        var collapsed = Separator.Replace(NonAlphanumeric.Replace(ascii, "-"), string.Empty);
        if (collapsed.Length > MaxLength)
        {
            var cut = collapsed[..MaxLength];
            var lastDash = cut.LastIndexOf('-');
            collapsed = lastDash > 0 ? cut[..lastDash] : cut;
        }

        return collapsed.Trim('-');
    }

    public Slug WithSuffix(string suffix)
    {
        Guard.NotNullOrWhiteSpace(suffix, nameof(suffix));
        var combined = $"{Value}-{suffix.ToLowerInvariant()}";
        if (combined.Length > MaxLength)
        {
            combined = combined[..MaxLength].Trim('-');
        }

        return new Slug(combined);
    }

    public bool Equals(Slug? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as Slug);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Value;

    public static implicit operator string(Slug slug) => slug.Value;
}
