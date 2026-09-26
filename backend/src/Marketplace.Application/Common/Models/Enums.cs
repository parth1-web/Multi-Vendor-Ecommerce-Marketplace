namespace Marketplace.Application.Common.Models;

/// <summary>Whitelisted sort options for the public product listing.</summary>
public enum ProductSortOption
{
    Newest = 0,
    PriceAsc = 1,
    PriceDesc = 2,
    Rating = 3,
    Popular = 4,
    NameAsc = 5,
    NameDesc = 6,
    Discount = 7
}

/// <summary>Whitelisted sort options for admin order listings.</summary>
public enum OrderSortOption
{
    Newest = 0,
    Oldest = 1,
    HighestTotal = 2,
    LowestTotal = 3
}

/// <summary>Whitelisted date range presets used by the analytics endpoints.</summary>
public enum DateRangePreset
{
    Last7Days = 0,
    Last30Days = 1,
    Last90Days = 2,
    ThisMonth = 3,
    LastMonth = 4,
    ThisYear = 5,
    Custom = 6
}
