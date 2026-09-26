namespace Marketplace.Domain.Orders;

/// <summary>
/// Immutable copy of the delivery address taken at checkout. Modelled as its own type (not
/// the customer's <c>UserAddress</c> entity) so a later edit to the address book can never
/// rewrite what a historical order shipped to.
/// </summary>
public sealed class OrderAddressSnapshot
{
    public OrderAddressSnapshot()
    {
        Label = string.Empty;
        RecipientName = string.Empty;
        PhoneNumber = string.Empty;
        Line1 = string.Empty;
        City = string.Empty;
        PostalCode = string.Empty;
        Country = string.Empty;
    }

    public OrderAddressSnapshot(
        string label,
        string recipientName,
        string phoneNumber,
        string line1,
        string? line2,
        string city,
        string? state,
        string postalCode,
        string country)
    {
        Label = label;
        RecipientName = recipientName;
        PhoneNumber = phoneNumber;
        Line1 = line1;
        Line2 = line2;
        City = city;
        State = state;
        PostalCode = postalCode;
        Country = country;
    }

    public string Label { get; private set; }

    public string RecipientName { get; private set; }

    public string PhoneNumber { get; private set; }

    public string Line1 { get; private set; }

    public string? Line2 { get; private set; }

    public string City { get; private set; }

    public string? State { get; private set; }

    public string PostalCode { get; private set; }

    public string Country { get; private set; }
}
