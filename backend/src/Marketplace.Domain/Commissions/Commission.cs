using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Commissions;

/// <summary>
/// The marketplace's cut of a single seller order. The rate is copied from the seller
/// order at creation time, so changing a seller's rate later never rewrites past earnings.
/// </summary>
public class Commission : Entity
{
    private Commission() => Currency = "USD";

    private Commission(Guid id, Guid sellerOrderId, Guid orderId, Guid sellerId, decimal rate, decimal grossAmount, string currency, DateTimeOffset now)
        : base(id)
    {
        SellerOrderId = sellerOrderId;
        OrderId = orderId;
        SellerId = sellerId;
        Rate = rate;
        GrossAmount = grossAmount;
        CommissionAmount = CalculateCommission(grossAmount, rate);
        SellerAmount = decimal.Round(grossAmount - CommissionAmount, 2, MidpointRounding.AwayFromZero);
        Currency = currency;
        Status = CommissionStatus.Pending;
        CreatedAt = now;
    }

    public Guid SellerOrderId { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid SellerId { get; private set; }

    /// <summary>Percentage rate snapshotted at checkout.</summary>
    public decimal Rate { get; private set; }

    /// <summary>Net merchandise value the commission is calculated on (after discount).</summary>
    public decimal GrossAmount { get; private set; }

    public decimal CommissionAmount { get; private set; }

    public decimal SellerAmount { get; private set; }

    public string Currency { get; private set; }

    public CommissionStatus Status { get; private set; }

    public Guid? PayoutId { get; private set; }

    public string? ReversalReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? AccruedAt { get; private set; }

    public static Commission Create(Guid sellerOrderId, Guid orderId, Guid sellerId, decimal rate, decimal grossAmount, string currency, DateTimeOffset now)
    {
        Guard.NotEmpty(sellerOrderId, nameof(sellerOrderId));
        Guard.NotEmpty(orderId, nameof(orderId));
        Guard.NotEmpty(sellerId, nameof(sellerId));
        Guard.InRange(rate, 0m, 100m, nameof(rate));
        Guard.GreaterThanOrEqualToZero(grossAmount, nameof(grossAmount));

        return new Commission(
            SequentialGuid.New(now),
            sellerOrderId,
            orderId,
            sellerId,
            rate,
            decimal.Round(grossAmount, 2),
            string.IsNullOrWhiteSpace(currency) ? "USD" : currency.ToUpperInvariant(),
            now);
    }

    public void Accrue(DateTimeOffset now)
    {
        if (Status != CommissionStatus.Pending)
        {
            return;
        }

        Status = CommissionStatus.Accrued;
        AccruedAt = now;
        UpdatedAt = now;
    }

    public void MarkPayable(DateTimeOffset now)
    {
        if (Status is not (CommissionStatus.Accrued or CommissionStatus.Pending))
        {
            return;
        }

        Status = CommissionStatus.Payable;
        UpdatedAt = now;
    }

    public void MarkPaid(Guid payoutId, DateTimeOffset now)
    {
        if (Status is not (CommissionStatus.Payable or CommissionStatus.Accrued))
        {
            throw new InvalidStateTransitionException(nameof(Commission), Status.ToString(), CommissionStatus.Paid.ToString());
        }

        Status = CommissionStatus.Paid;
        PayoutId = payoutId;
        UpdatedAt = now;
    }

    public void Reverse(string reason, DateTimeOffset now)
    {
        if (Status == CommissionStatus.Reversed)
        {
            return;
        }

        if (Status == CommissionStatus.Paid)
        {
            throw new BusinessRuleException("A paid commission cannot be reversed; raise a negative payout instead.");
        }

        Status = CommissionStatus.Reversed;
        ReversalReason = reason;
        UpdatedAt = now;
    }

    /// <summary>Commission on 10 000 at 10 % is 1 000; the seller keeps 9 000.</summary>
    public static decimal CalculateCommission(decimal grossAmount, decimal rate) =>
        decimal.Round(grossAmount * rate / 100m, 2, MidpointRounding.AwayFromZero);
}
