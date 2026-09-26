using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Commissions;

/// <summary>
/// The marketplace's commission rules. Kept separate from the <see cref="Commission"/>
/// entity so the arithmetic can be exhaustively unit tested.
/// </summary>
public static class CommissionCalculator
{
    /// <summary>
    /// Sale of 10 000 at 10 % → marketplace 1 000, seller 9 000.
    /// </summary>
    public static CommissionAmounts Calculate(decimal sellerOrderNetAmount, decimal rate)
    {
        if (sellerOrderNetAmount < 0m)
        {
            throw new ValidationException(nameof(sellerOrderNetAmount), "Cannot be negative.");
        }

        if (rate is < 0m or > 100m)
        {
            throw new ValidationException(nameof(rate), "The commission rate must be between 0 and 100.");
        }

        var commission = Commission.CalculateCommission(sellerOrderNetAmount, rate);
        var seller = decimal.Round(sellerOrderNetAmount - commission, 2, MidpointRounding.AwayFromZero);
        return new CommissionAmounts(commission, seller);
    }

    /// <summary>Total marketplace revenue for a set of seller-order net amounts.</summary>
    public static decimal TotalMarketplaceRevenue(IEnumerable<(decimal NetAmount, decimal Rate)> lines) =>
        decimal.Round(lines.Sum(l => Calculate(l.NetAmount, l.Rate).CommissionAmount), 2, MidpointRounding.AwayFromZero);
}

/// <summary>Split of a seller order between the marketplace and the seller.</summary>
public readonly record struct CommissionAmounts(decimal CommissionAmount, decimal SellerAmount)
{
    public decimal Total => decimal.Round(CommissionAmount + SellerAmount, 2, MidpointRounding.AwayFromZero);
}
