using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Orders;

/// <summary>A single priced line offered to the pricing engine.</summary>
/// <param name="ProductId">Product the line refers to.</param>
/// <param name="ProductVariantId">Variant (SKU) being purchased.</param>
/// <param name="SellerId">Owning seller, used for grouping and seller-scoped coupons.</param>
/// <param name="UnitPrice">Authoritative unit price from the catalogue.</param>
/// <param name="Quantity">Units requested.</param>
public readonly record struct PricedLine(Guid ProductId, Guid ProductVariantId, Guid SellerId, decimal UnitPrice, int Quantity)
{
    public decimal LineTotal => decimal.Round(UnitPrice * Quantity, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Totals for one seller, produced while pricing the basket.</summary>
public readonly record struct SellerSubtotal(Guid SellerId, decimal Subtotal, int ItemCount);

/// <summary>Complete, authoritative basket pricing result.</summary>
public readonly record struct OrderTotals(
    decimal Subtotal,
    decimal DiscountAmount,
    decimal ShippingAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    IReadOnlyList<SellerSubtotal> SellerBreakdown,
    IReadOnlyDictionary<Guid, decimal> DiscountBySeller);

/// <summary>
/// The single place where an order's money is decided. The frontend never sends totals —
/// it only asks for a quote, and the same engine produces the committed order.
/// </summary>
public static class OrderPricingCalculator
{
    /// <summary>
    /// Computes marketplace totals from authoritative line prices.
    /// </summary>
    /// <param name="lines">Basket lines with prices taken from the catalogue.</param>
    /// <param name="discountBySeller">Pre-allocated per-seller discount (from a validated coupon).</param>
    /// <param name="standardShippingCost">Flat shipping charged per seller that has a remainder.</param>
    /// <param name="freeShippingThreshold">Per-seller subtotal above which shipping is waived.</param>
    /// <param name="taxRate">Percentage tax applied after discount.</param>
    public static OrderTotals Calculate(
        IReadOnlyCollection<PricedLine> lines,
        IReadOnlyDictionary<Guid, decimal>? discountBySeller,
        decimal standardShippingCost,
        decimal freeShippingThreshold,
        decimal taxRate)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            throw new ValidationException(nameof(lines), "A basket must contain at least one line.");
        }

        if (standardShippingCost < 0m)
        {
            throw new ValidationException(nameof(standardShippingCost), "Shipping cost cannot be negative.");
        }

        if (taxRate is < 0m or > 100m)
        {
            throw new ValidationException(nameof(taxRate), "The tax rate must be between 0 and 100.");
        }

        var subtotal = decimal.Round(lines.Sum(l => l.LineTotal), 2, MidpointRounding.AwayFromZero);
        if (subtotal <= 0m)
        {
            throw new ValidationException(nameof(lines), "The basket total must be greater than zero.");
        }

        var breakdown = lines
            .GroupBy(l => l.SellerId)
            .Select(g => new SellerSubtotal(g.Key, decimal.Round(g.Sum(l => l.LineTotal), 2), g.Sum(l => l.Quantity)))
            .OrderBy(s => s.SellerId)
            .ToList();

        // Allocate whatever discount was supplied, capping it per seller at their subtotal.
        var allocated = new Dictionary<Guid, decimal>();
        decimal discountTotal = 0m;

        if (discountBySeller is not null)
        {
            foreach (var seller in breakdown)
            {
                var requested = discountBySeller.TryGetValue(seller.SellerId, out var value) ? value : 0m;
                var applied = decimal.Round(Math.Clamp(requested, 0m, seller.Subtotal), 2, MidpointRounding.AwayFromZero);
                if (applied > 0m)
                {
                    allocated[seller.SellerId] = applied;
                    discountTotal += applied;
                }
            }
        }

        var payableSubtotal = decimal.Round(subtotal - discountTotal, 2, MidpointRounding.AwayFromZero);
        if (payableSubtotal < 0m)
        {
            throw new BusinessRuleException("The discount exceeds the basket total.");
        }

        // Free shipping is evaluated per seller, and waived entirely once the basket
        // reaches the threshold — a marketplace must not double-charge the customer.
        var shipping = 0m;
        if (payableSubtotal < freeShippingThreshold)
        {
            shipping = decimal.Round(
                breakdown.Count(s => s.Subtotal - allocated.GetValueOrDefault(s.SellerId) > 0m) * standardShippingCost,
                2,
                MidpointRounding.AwayFromZero);
        }

        var tax = decimal.Round(payableSubtotal * taxRate / 100m, 2, MidpointRounding.AwayFromZero);
        var total = decimal.Round(payableSubtotal + shipping + tax, 2, MidpointRounding.AwayFromZero);

        if (total <= 0m)
        {
            throw new BusinessRuleException("The computed order total must be greater than zero.");
        }

        return new OrderTotals(subtotal, discountTotal, shipping, tax, total, breakdown, allocated);
    }

    /// <summary>
    /// Distributes a coupon discount across sellers, largest-remainder style, so the
    /// per-seller amounts always sum exactly to the marketplace discount.
    /// </summary>
    public static Dictionary<Guid, decimal> AllocateDiscount(IReadOnlyCollection<SellerSubtotal> breakdown, decimal totalDiscount)
    {
        ArgumentNullException.ThrowIfNull(breakdown);

        var result = new Dictionary<Guid, decimal>();
        if (totalDiscount <= 0m || breakdown.Count == 0)
        {
            return result;
        }

        var pool = decimal.Round(totalDiscount, 2, MidpointRounding.AwayFromZero);
        var eligible = breakdown.Where(s => s.Subtotal > 0m).ToList();
        if (eligible.Count == 0)
        {
            return result;
        }

        var totalSubtotal = eligible.Sum(s => s.Subtotal);
        var allocated = 0m;

        foreach (var seller in eligible)
        {
            var share = decimal.Round(pool * seller.Subtotal / totalSubtotal, 2, MidpointRounding.AwayFromZero);
            share = Math.Min(share, seller.Subtotal);
            result[seller.SellerId] = share;
            allocated += share;
        }

        // Give the rounding remainder to the largest seller so the sum is exact.
        var remainder = decimal.Round(pool - allocated, 2, MidpointRounding.AwayFromZero);
        if (remainder != 0m)
        {
            var target = eligible.OrderByDescending(s => s.Subtotal).First();
            var adjusted = Math.Clamp(result[target.SellerId] + remainder, 0m, target.Subtotal);
            result[target.SellerId] = decimal.Round(adjusted, 2, MidpointRounding.AwayFromZero);
        }

        return result;
    }
}
