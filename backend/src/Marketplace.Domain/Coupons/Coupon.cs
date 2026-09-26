using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Coupons;

/// <summary>
/// A discount coupon. Scope, limits and date window are enforced on the aggregate, and
/// the discount maths itself lives in <see cref="CouponCalculator"/>.
/// </summary>
public class Coupon : Entity
{
    private readonly List<Guid> _productIds = [];

    private Coupon()
    {
        Code = string.Empty;
        Description = string.Empty;
    }

    private Coupon(Guid id, Guid? sellerId, CouponScope scope, string code, string description, CouponDiscountType discountType, decimal discountValue, decimal? minimumOrderAmount, decimal? maximumDiscountAmount, int? usageLimit, int perUserLimit, DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset now)
        : base(id)
    {
        SellerId = sellerId;
        Scope = scope;
        Code = code.Trim().ToUpperInvariant();
        Description = description?.Trim() ?? string.Empty;
        DiscountType = discountType;
        DiscountValue = discountValue;
        MinimumOrderAmount = minimumOrderAmount;
        MaximumDiscountAmount = maximumDiscountAmount;
        UsageLimit = usageLimit;
        PerUserLimit = perUserLimit;
        StartsAt = startsAt;
        EndsAt = endsAt;
        Status = CouponStatus.Active;
        CreatedAt = now;
    }

    public Guid? SellerId { get; private set; }

    public CouponScope Scope { get; private set; }

    public string Code { get; private set; }

    public string Description { get; private set; }

    public CouponDiscountType DiscountType { get; private set; }

    /// <summary>Percentage (0–100) or a fixed amount, depending on <see cref="DiscountType"/>.</summary>
    public decimal DiscountValue { get; private set; }

    public decimal? MinimumOrderAmount { get; private set; }

    /// <summary>Caps a percentage discount so a large basket cannot produce an absurd discount.</summary>
    public decimal? MaximumDiscountAmount { get; private set; }

    public int? UsageLimit { get; private set; }

    public int UsageCount { get; private set; }

    public int PerUserLimit { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public CouponStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<Guid> ProductIds => _productIds.AsReadOnly();

    public bool IsGlobal => Scope == CouponScope.Global;

    public static Coupon Create(
        Guid? sellerId,
        CouponScope scope,
        string code,
        string description,
        CouponDiscountType discountType,
        decimal discountValue,
        decimal? minimumOrderAmount,
        decimal? maximumDiscountAmount,
        int? usageLimit,
        int perUserLimit,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(code, nameof(code));

        if (scope == CouponScope.Seller && sellerId is null)
        {
            throw new ValidationException(nameof(sellerId), "A seller-scoped coupon requires a seller.");
        }

        if (scope == CouponScope.Global && sellerId is not null)
        {
            throw new ValidationException(nameof(sellerId), "A global coupon must not be tied to a seller.");
        }

        ValidateValues(discountType, discountValue, minimumOrderAmount, maximumDiscountAmount, usageLimit, perUserLimit, startsAt, endsAt, now);

        return new Coupon(
            SequentialGuid.New(now),
            sellerId,
            scope,
            code,
            description,
            discountType,
            decimal.Round(discountValue, 2),
            minimumOrderAmount,
            maximumDiscountAmount,
            usageLimit,
            perUserLimit,
            startsAt,
            endsAt,
            now);
    }

    public void Update(
        string description,
        decimal discountValue,
        decimal? minimumOrderAmount,
        decimal? maximumDiscountAmount,
        int? usageLimit,
        int perUserLimit,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        DateTimeOffset now)
    {
        ValidateValues(DiscountType, discountValue, minimumOrderAmount, maximumDiscountAmount, usageLimit, perUserLimit, startsAt, endsAt, now);

        Description = description?.Trim() ?? string.Empty;
        DiscountValue = decimal.Round(discountValue, 2);
        MinimumOrderAmount = minimumOrderAmount;
        MaximumDiscountAmount = maximumDiscountAmount;
        UsageLimit = usageLimit;
        PerUserLimit = perUserLimit;
        StartsAt = startsAt;
        EndsAt = endsAt;
        UpdatedAt = now;
    }

    public void RestrictToProducts(IEnumerable<Guid> productIds, DateTimeOffset now)
    {
        _productIds.Clear();
        _productIds.AddRange(productIds.Distinct());
        UpdatedAt = now;
    }

    public void SetStatus(CouponStatus status, DateTimeOffset now)
    {
        Status = status;
        UpdatedAt = now;
    }

    /// <summary>Increments the global counter. Throws when the limit would be exceeded.</summary>
    public void RecordUsage(DateTimeOffset now)
    {
        if (UsageLimit is not null && UsageCount >= UsageLimit.Value)
        {
            throw new BusinessRuleException("This coupon has reached its usage limit.");
        }

        UsageCount++;
        if (UsageLimit is not null && UsageCount >= UsageLimit.Value)
        {
            Status = CouponStatus.Exhausted;
        }

        UpdatedAt = now;
    }

    public bool IsActiveOn(DateTimeOffset now) => Status == CouponStatus.Active && now >= StartsAt && now <= EndsAt;

    public bool IsExpired(DateTimeOffset now) => now > EndsAt;

    public bool IsExhausted => UsageLimit is not null && UsageCount >= UsageLimit.Value;

    public bool AppliesToProduct(Guid productId) => _productIds.Count == 0 || _productIds.Contains(productId);

    private static void ValidateValues(
        CouponDiscountType discountType,
        decimal discountValue,
        decimal? minimumOrderAmount,
        decimal? maximumDiscountAmount,
        int? usageLimit,
        int perUserLimit,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        DateTimeOffset now)
    {
        if (discountType == CouponDiscountType.Percentage)
        {
            Guard.InRange(discountValue, 0.01m, 100m, nameof(discountValue));
        }
        else
        {
            Guard.GreaterThanZero((int)decimal.Round(discountValue, 0), nameof(discountValue));
        }

        if (minimumOrderAmount is not null)
        {
            Guard.GreaterThanOrEqualToZero(minimumOrderAmount.Value, nameof(minimumOrderAmount));
        }

        if (maximumDiscountAmount is not null)
        {
            Guard.GreaterThanOrEqualToZero(maximumDiscountAmount.Value, nameof(maximumDiscountAmount));
        }

        if (usageLimit is not null && usageLimit.Value <= 0)
        {
            throw new ValidationException(nameof(usageLimit), "The usage limit must be greater than zero.");
        }

        if (perUserLimit <= 0)
        {
            throw new ValidationException(nameof(perUserLimit), "The per-user limit must be greater than zero.");
        }

        if (endsAt <= startsAt)
        {
            throw new ValidationException(nameof(endsAt), "The end date must be after the start date.");
        }

        if (endsAt <= now)
        {
            throw new ValidationException(nameof(endsAt), "The end date must be in the future.");
        }
    }
}
