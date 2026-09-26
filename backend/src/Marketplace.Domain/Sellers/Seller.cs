using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Sellers;

/// <summary>
/// A seller account. The lifecycle (Pending → Active → Suspended) is enforced here so
/// no service can bypass the approval gate.
/// </summary>
public class Seller : Entity
{
    private Seller()
    {
        BusinessName = string.Empty;
        LegalName = string.Empty;
        PhoneNumber = string.Empty;
        Address = string.Empty;
        TaxIdentityNumber = string.Empty;
        BankAccountName = string.Empty;
    }

    private Seller(Guid id, Guid userId, string businessName, string? legalName, string? phoneNumber, string? address, string? taxIdentityNumber, string? bankAccountName, string? bankAccountNumber, decimal defaultCommissionRate, DateTimeOffset now)
        : base(id)
    {
        UserId = userId;
        BusinessName = businessName;
        LegalName = legalName;
        PhoneNumber = phoneNumber;
        Address = address;
        TaxIdentityNumber = taxIdentityNumber;
        BankAccountName = bankAccountName;
        BankAccountNumber = bankAccountNumber;
        DefaultCommissionRate = defaultCommissionRate;
        Status = SellerStatus.Pending;
        AppliedAt = now;
        CreatedAt = now;
    }

    public Guid UserId { get; private set; }

    public SellerStatus Status { get; private set; }

    public string BusinessName { get; private set; }

    public string? LegalName { get; private set; }

    public string? PhoneNumber { get; private set; }

    public string? Address { get; private set; }

    public string? TaxIdentityNumber { get; private set; }

    public string? BankAccountName { get; private set; }

    /// <summary>Stored masked — only the last four characters are retained.</summary>
    public string? BankAccountNumber { get; private set; }

    /// <summary>Marketplace cut applied to this seller's sales until changed by an admin.</summary>
    public decimal DefaultCommissionRate { get; private set; }

    public string? RejectionReason { get; private set; }

    public string? SuspensionReason { get; private set; }

    public DateTimeOffset AppliedAt { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public DateTimeOffset? SuspendedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmax</c>).</summary>
    public byte[] RowVersion { get; private set; } = [];

    public bool IsActive => Status == SellerStatus.Active;

    public bool CanListProducts => Status == SellerStatus.Active;

    public static Seller Apply(
        Guid userId,
        string businessName,
        string? legalName,
        string? phoneNumber,
        string? address,
        string? taxIdentityNumber,
        string? bankAccountName,
        string? bankAccountNumber,
        decimal defaultCommissionRate,
        DateTimeOffset now)
    {
        Guard.NotEmpty(userId, nameof(userId));
        Guard.NotNullOrWhiteSpace(businessName, nameof(businessName));
        Guard.InRange(defaultCommissionRate, 0m, 100m, nameof(defaultCommissionRate));

        return new Seller(
            SequentialGuid.New(now),
            userId,
            businessName.Trim(),
            legalName?.Trim(),
            phoneNumber?.Trim(),
            address?.Trim(),
            taxIdentityNumber?.Trim(),
            bankAccountName?.Trim(),
            MaskAccountNumber(bankAccountNumber),
            defaultCommissionRate,
            now);
    }

    public void Approve(decimal commissionRate, DateTimeOffset now)
    {
        Guard.InRange(commissionRate, 0m, 100m, nameof(commissionRate));
        EnsureNotSuspended();

        Status = SellerStatus.Active;
        DefaultCommissionRate = commissionRate;
        ApprovedAt = now;
        RejectionReason = null;
        SuspensionReason = null;
        UpdatedAt = now;
    }

    public void Reject(string reason, DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(reason, nameof(reason));
        EnsureNotActive();

        Status = SellerStatus.Rejected;
        RejectionReason = reason.Trim();
        UpdatedAt = now;
    }

    public void Suspend(string reason, DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(reason, nameof(reason));
        EnsureNotSuspended();

        Status = SellerStatus.Suspended;
        SuspensionReason = reason.Trim();
        SuspendedAt = now;
        UpdatedAt = now;
    }

    public void Resume(DateTimeOffset now)
    {
        if (Status != SellerStatus.Suspended)
        {
            throw new InvalidStateTransitionException(nameof(Seller), Status.ToString(), SellerStatus.Active.ToString());
        }

        Status = SellerStatus.Active;
        SuspensionReason = null;
        SuspendedAt = null;
        UpdatedAt = now;
    }

    public void UpdateProfile(
        string businessName,
        string? legalName,
        string? phoneNumber,
        string? address,
        string? taxIdentityNumber,
        string? bankAccountName,
        string? bankAccountNumber,
        DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(businessName, nameof(businessName));
        EnsureNotSuspended();

        BusinessName = businessName.Trim();
        LegalName = legalName?.Trim();
        PhoneNumber = phoneNumber?.Trim();
        Address = address?.Trim();
        TaxIdentityNumber = taxIdentityNumber?.Trim();
        BankAccountName = bankAccountName?.Trim();

        if (!string.IsNullOrWhiteSpace(bankAccountNumber))
        {
            BankAccountNumber = MaskAccountNumber(bankAccountNumber);
        }

        UpdatedAt = now;
    }

    public void ChangeCommissionRate(decimal rate, DateTimeOffset now)
    {
        Guard.InRange(rate, 0m, 100m, nameof(rate));
        DefaultCommissionRate = rate;
        UpdatedAt = now;
    }

    private void EnsureNotActive()
    {
        if (Status == SellerStatus.Active)
        {
            throw new InvalidStateTransitionException(nameof(Seller), Status.ToString(), SellerStatus.Rejected.ToString());
        }
    }

    private void EnsureNotSuspended()
    {
        if (Status == SellerStatus.Suspended)
        {
            throw new BusinessRuleException("A suspended seller must be resumed before making changes.");
        }
    }

    internal static string? MaskAccountNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 4 ? "****" : $"****{trimmed[^4..]}";
    }
}
