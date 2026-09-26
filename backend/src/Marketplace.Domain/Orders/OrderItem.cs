using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Orders;

/// <summary>
/// A purchased line. Name, image, SKU and unit price are <em>snapshots</em>: later edits
/// to the catalogue must never rewrite what the customer actually bought.
/// </summary>
public class OrderItem : Entity
{
    private OrderItem()
    {
        ProductName = string.Empty;
        VariantName = string.Empty;
        Sku = string.Empty;
    }

    private OrderItem(
        Guid id,
        Guid orderId,
        Guid sellerOrderId,
        Guid productId,
        Guid productVariantId,
        Guid sellerId,
        Guid categoryId,
        string productName,
        string? productImage,
        string variantName,
        string sku,
        int quantity,
        decimal unitPrice,
        decimal lineTotal,
        DateTimeOffset now)
        : base(id)
    {
        OrderId = orderId;
        SellerOrderId = sellerOrderId;
        ProductId = productId;
        ProductVariantId = productVariantId;
        SellerId = sellerId;
        CategoryId = categoryId;
        ProductName = productName;
        ProductImage = productImage;
        VariantName = variantName;
        Sku = sku;
        Quantity = quantity;
        UnitPrice = unitPrice;
        LineTotal = lineTotal;
        CreatedAt = now;
    }

    public Guid OrderId { get; private set; }

    /// <summary>Owning marketplace order. Navigation only — set by EF Core.</summary>
    public Order? Order { get; internal set; }

    public Guid SellerOrderId { get; private set; }

    /// <summary>Owning seller sub-order. Navigation only — set by EF Core.</summary>
    public SellerOrder? SellerOrder { get; internal set; }

    public Guid ProductId { get; private set; }

    public Guid ProductVariantId { get; private set; }

    public Guid SellerId { get; private set; }

    public Guid CategoryId { get; private set; }

    public string ProductName { get; private set; }

    public string? ProductImage { get; private set; }

    public string VariantName { get; private set; }

    public string Sku { get; private set; }

    public int Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal LineTotal { get; private set; }

    /// <summary>Discount allocated to this line, so the seller's earnings stay correct.</summary>
    public decimal DiscountAmount { get; private set; }

    public bool IsReviewed { get; private set; }

    public bool IsRefundRequested { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static OrderItem Create(
        Guid orderId,
        Guid sellerOrderId,
        Guid productId,
        Guid productVariantId,
        Guid sellerId,
        Guid categoryId,
        string productName,
        string? productImage,
        string variantName,
        string sku,
        int quantity,
        decimal unitPrice,
        decimal lineTotal,
        DateTimeOffset now)
    {
        Guard.NotEmpty(orderId, nameof(orderId));
        Guard.NotEmpty(sellerOrderId, nameof(sellerOrderId));
        Guard.NotEmpty(productId, nameof(productId));
        Guard.NotEmpty(sellerId, nameof(sellerId));
        Guard.NotNullOrWhiteSpace(productName, nameof(productName));
        Guard.GreaterThanZero(quantity, nameof(quantity));
        Guard.InRange(unitPrice, 0.01m, 10_000_000m, nameof(unitPrice));

        return new OrderItem(
            SequentialGuid.New(now),
            orderId,
            sellerOrderId,
            productId,
            productVariantId,
            sellerId,
            categoryId,
            productName.Trim(),
            productImage,
            variantName?.Trim() ?? string.Empty,
            sku?.Trim() ?? string.Empty,
            quantity,
            decimal.Round(unitPrice, 2),
            decimal.Round(lineTotal, 2),
            now);
    }

    public void AllocateDiscount(decimal amount, DateTimeOffset now)
    {
        DiscountAmount = decimal.Round(Math.Clamp(amount, 0m, LineTotal), 2, MidpointRounding.AwayFromZero);
        _ = now;
    }

    public void MarkReviewed()
    {
        if (IsReviewed)
        {
            throw new BusinessRuleException("This order item has already been reviewed.");
        }

        IsReviewed = true;
    }

    public void MarkRefundRequested()
    {
        if (IsRefundRequested)
        {
            throw new DuplicateEntityException(nameof(OrderItem), nameof(IsRefundRequested), Id.ToString());
        }

        IsRefundRequested = true;
    }

    public bool IsReturnable(DateTimeOffset now) => IsRefundEligible(OrderStatus.Delivered, now);

    internal bool IsRefundEligible(OrderStatus orderStatus, DateTimeOffset now) =>
        orderStatus is OrderStatus.Delivered or OrderStatus.Completed &&
        !IsRefundRequested &&
        now <= CreatedAt.AddDays(30);
}
