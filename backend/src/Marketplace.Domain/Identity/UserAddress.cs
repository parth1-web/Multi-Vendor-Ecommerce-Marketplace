using Marketplace.Domain.Common;

namespace Marketplace.Domain.Identity;

public class UserAddress : Entity
{
    private UserAddress()
    {
        Label = string.Empty;
        RecipientName = string.Empty;
        PhoneNumber = string.Empty;
        Line1 = string.Empty;
        City = string.Empty;
        Country = string.Empty;
        PostalCode = string.Empty;
    }

    private UserAddress(Guid id, Guid userId, string label, string recipientName, string phoneNumber, string line1, string? line2, string city, string? state, string postalCode, string country, bool isDefault, DateTimeOffset now)
        : base(id)
    {
        UserId = userId;
        Label = label;
        RecipientName = recipientName;
        PhoneNumber = phoneNumber;
        Line1 = line1;
        Line2 = line2;
        City = city;
        State = state;
        PostalCode = postalCode;
        Country = country;
        IsDefault = isDefault;
        CreatedAt = now;
    }

    public Guid UserId { get; private set; }

    public string Label { get; private set; }

    public string RecipientName { get; private set; }

    public string PhoneNumber { get; private set; }

    public string Line1 { get; private set; }

    public string? Line2 { get; private set; }

    public string City { get; private set; }

    public string? State { get; private set; }

    public string PostalCode { get; private set; }

    public string Country { get; private set; }

    public bool IsDefault { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static UserAddress Create(
        Guid userId,
        string label,
        string recipientName,
        string phoneNumber,
        string line1,
        string? line2,
        string city,
        string? state,
        string postalCode,
        string country,
        bool isDefault,
        DateTimeOffset now)
    {
        Guard.NotEmpty(userId, nameof(userId));
        Guard.NotNullOrWhiteSpace(recipientName, nameof(recipientName));
        Guard.NotNullOrWhiteSpace(phoneNumber, nameof(phoneNumber));
        Guard.NotNullOrWhiteSpace(line1, nameof(line1));
        Guard.NotNullOrWhiteSpace(city, nameof(city));
        Guard.NotNullOrWhiteSpace(postalCode, nameof(postalCode));
        Guard.NotNullOrWhiteSpace(country, nameof(country));

        return new UserAddress(
            SequentialGuid.New(),
            userId,
            label?.Trim() ?? "Home",
            recipientName.Trim(),
            phoneNumber.Trim(),
            line1.Trim(),
            line2?.Trim(),
            city.Trim(),
            state?.Trim(),
            postalCode.Trim(),
            country.Trim().ToUpperInvariant(),
            isDefault,
            now)
        {
            UpdatedAt = now
        };
    }

    public void Update(string label, string recipientName, string phoneNumber, string line1, string? line2, string city, string? state, string postalCode, string country, DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(recipientName, nameof(recipientName));
        Guard.NotNullOrWhiteSpace(line1, nameof(line1));

        Label = label?.Trim() ?? Label;
        RecipientName = recipientName.Trim();
        PhoneNumber = phoneNumber?.Trim() ?? PhoneNumber;
        Line1 = line1.Trim();
        Line2 = line2?.Trim();
        City = city?.Trim() ?? City;
        State = state?.Trim();
        PostalCode = postalCode?.Trim() ?? PostalCode;
        Country = country?.Trim().ToUpperInvariant() ?? Country;
        UpdatedAt = now;
    }

    public void MakeDefault()
    {
        IsDefault = true;
    }

    public void ClearDefault()
    {
        IsDefault = false;
    }
}
