using FluentValidation;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Catalog.Abstractions;
using Marketplace.Application.Modules.Catalog.DTOs;
using Marketplace.Application.Modules.Coupons.DTOs;
using Marketplace.Application.Modules.Inventory.Abstractions;
using Marketplace.Application.Modules.Notifications.DTOs;
using Marketplace.Application.Modules.Orders.Abstractions;
using Marketplace.Application.Modules.Orders.DTOs;
using Marketplace.Application.Modules.Payments.DTOs;
using Marketplace.Application.Modules.Reviews.DTOs;
using Marketplace.Application.Modules.Sellers.DTOs;
using Marketplace.Domain.Enums;

namespace Marketplace.Application.Modules.Validation;

// ---------------------------------------------------------------------------
// Sellers
// ---------------------------------------------------------------------------

public sealed class ApplySellerRequestValidator : AbstractValidator<ApplySellerRequest>
{
    public ApplySellerRequestValidator()
    {
        RuleFor(x => x.BusinessName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PhoneNumber).NotEmpty().Matches("^[+0-9 ()-]{6,20}$");
        RuleFor(x => x.Address).NotEmpty().MaximumLength(500);
        RuleFor(x => x.StoreName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.StoreDescription).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.BankAccountNumber).Matches("^[0-9A-Za-z -]{4,34}$").When(x => !string.IsNullOrWhiteSpace(x.BankAccountNumber));
    }
}

public sealed class UpdateSellerRequestValidator : AbstractValidator<UpdateSellerRequest>
{
    public UpdateSellerRequestValidator()
    {
        RuleFor(x => x.BusinessName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PhoneNumber).Matches("^[+0-9 ()-]{6,20}$").When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
    }
}

public sealed class UpdateSellerStatusRequestValidator : AbstractValidator<UpdateSellerStatusRequest>
{
    public UpdateSellerStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required when rejecting or suspending a seller.")
            .When(x => x.Status is SellerStatus.Rejected or SellerStatus.Suspended);

        RuleFor(x => x.CommissionRate)
            .InclusiveBetween(0m, 100m)
            .When(x => x.CommissionRate.HasValue);
    }
}

public sealed class UpdateStoreRequestValidator : AbstractValidator<UpdateStoreRequest>
{
    public UpdateStoreRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.SupportEmail).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.SupportEmail));
        RuleFor(x => x.FoundedYear).InclusiveBetween(1800, 2100).When(x => x.FoundedYear.HasValue);
    }
}

// ---------------------------------------------------------------------------
// Catalog
// ---------------------------------------------------------------------------

public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Slug).Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$").When(x => !string.IsNullOrWhiteSpace(x.Slug));
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Slug).Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$").When(x => !string.IsNullOrWhiteSpace(x.Slug));
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ShortDescription).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(20000);
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.BasePrice).InclusiveBetween(0.01m, 10_000_000m);
        RuleFor(x => x.CompareAtPrice)
            .GreaterThan(x => x.BasePrice)
            .When(x => x.CompareAtPrice.HasValue);
        RuleFor(x => x.Slug).Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$").When(x => !string.IsNullOrWhiteSpace(x.Slug));
        RuleFor(x => x.Images).NotEmpty().WithMessage("At least one product image is required.");
        RuleFor(x => x.Images).Must(images => images.Count <= 10).WithMessage("A product may have at most 10 images.");
        RuleFor(x => x.Variants).NotEmpty().WithMessage("At least one product variant is required.");
        RuleFor(x => x.Variants).Must(v => v.Count <= 50).WithMessage("A product may have at most 50 variants.");
        RuleForEach(x => x.Variants).ChildRules(v =>
        {
            v.RuleFor(x => x.Sku).NotEmpty().MaximumLength(64);
            v.RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            v.RuleFor(x => x.InitialStock).GreaterThanOrEqualTo(0);
        });
    }
}

public sealed class UpdateProductRequestValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ShortDescription).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(20000);
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.BasePrice).InclusiveBetween(0.01m, 10_000_000m);
        RuleFor(x => x.CompareAtPrice).GreaterThan(x => x.BasePrice).When(x => x.CompareAtPrice.HasValue);
    }
}

public sealed class ProductApprovalRequestValidator : AbstractValidator<ProductApprovalRequest>
{
    public ProductApprovalRequestValidator() =>
        RuleFor(x => x.Note).NotEmpty().MaximumLength(1000).When(x => !x.Approve);
}

public sealed class AddProductVariantRequestValidator : AbstractValidator<AddProductVariantRequest>
{
    public AddProductVariantRequestValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.InitialStock).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Price).InclusiveBetween(0.01m, 10_000_000m).When(x => x.Price.HasValue);
    }
}

// ---------------------------------------------------------------------------
// Inventory
// ---------------------------------------------------------------------------

public sealed class AdjustStockRequestValidator : AbstractValidator<AdjustStockRequest>
{
    public AdjustStockRequestValidator()
    {
        RuleFor(x => x.Delta).NotEqual(0).WithMessage("The adjustment cannot be zero.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(300).WithMessage("A reason is required for every stock adjustment.");
    }
}

public sealed class SetThresholdRequestValidator : AbstractValidator<SetThresholdRequest>
{
    public SetThresholdRequestValidator() => RuleFor(x => x.LowStockThreshold).InclusiveBetween(0, 100000);
}

// ---------------------------------------------------------------------------
// Cart, checkout, orders, addresses
// ---------------------------------------------------------------------------

public sealed class AddCartItemRequestValidator : AbstractValidator<AddCartItemRequest>
{
    public AddCartItemRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.ProductVariantId).NotEmpty();
        RuleFor(x => x.Quantity).InclusiveBetween(1, 100);
    }
}

public sealed class UpdateCartItemRequestValidator : AbstractValidator<UpdateCartItemRequest>
{
    public UpdateCartItemRequestValidator() => RuleFor(x => x.Quantity).InclusiveBetween(1, 100);
}

public sealed class QuoteRequestValidator : AbstractValidator<QuoteRequest>
{
    public QuoteRequestValidator() => RuleFor(x => x.CouponCode).MaximumLength(50);
}

public sealed class CheckoutRequestValidator : AbstractValidator<CheckoutRequest>
{
    public CheckoutRequestValidator()
    {
        RuleFor(x => x.ShippingAddressId).NotEmpty().WithMessage("A delivery address is required.");
        RuleFor(x => x.PaymentMethod).NotEmpty().MaximumLength(40);
        RuleFor(x => x.CouponCode).MaximumLength(50);
        RuleFor(x => x.CustomerNote).MaximumLength(1000);
        RuleFor(x => x.IdempotencyKey).MaximumLength(100);
    }
}

public sealed class UpdateOrderStatusRequestValidator : AbstractValidator<UpdateOrderStatusRequest>
{
    public UpdateOrderStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.TrackingNumber).NotEmpty().MaximumLength(100)
            .When(x => x.Status == SellerOrderStatus.Shipped);
        RuleFor(x => x.CarrierName).NotEmpty().MaximumLength(100)
            .When(x => x.Status == SellerOrderStatus.Shipped);
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public sealed class CancelOrderRequestValidator : AbstractValidator<CancelOrderRequest>
{
    public CancelOrderRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

public sealed class CreateAddressRequestValidator : AbstractValidator<CreateAddressRequest>
{
    public CreateAddressRequestValidator()
    {
        RuleFor(x => x.Label).NotEmpty().MaximumLength(60);
        RuleFor(x => x.RecipientName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.PhoneNumber).NotEmpty().Matches("^[+0-9 ()-]{6,20}$");
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Line2).MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Country).NotEmpty().Length(2).WithMessage("Use a two-letter ISO country code.");
    }
}

public sealed class UpdateAddressRequestValidator : AbstractValidator<UpdateAddressRequest>
{
    public UpdateAddressRequestValidator()
    {
        RuleFor(x => x.Label).NotEmpty().MaximumLength(60);
        RuleFor(x => x.RecipientName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.PhoneNumber).NotEmpty().Matches("^[+0-9 ()-]{6,20}$");
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Country).NotEmpty().Length(2);
    }
}

// ---------------------------------------------------------------------------
// Payments and refunds
// ---------------------------------------------------------------------------

public sealed class CreatePaymentRequestValidator : AbstractValidator<CreatePaymentRequest>
{
    public CreatePaymentRequestValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).MaximumLength(100);
    }
}

public sealed class CreateRefundRequestValidator : AbstractValidator<CreateRefundRequest>
{
    public CreateRefundRequestValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.OrderItemIds).NotEmpty().WithMessage("Select at least one item.");
        RuleFor(x => x.OrderItemIds.Count).LessThanOrEqualTo(20);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}

public sealed class ReviewRefundRequestValidator : AbstractValidator<ReviewRefundRequest>
{
    public ReviewRefundRequestValidator()
    {
        RuleFor(x => x.Action).IsInEnum();
        RuleFor(x => x.Note).NotEmpty().MaximumLength(1000)
            .When(x => x.Action == ReviewRefundAction.Reject);
    }
}

// ---------------------------------------------------------------------------
// Reviews
// ---------------------------------------------------------------------------

public sealed class CreateReviewRequestValidator : AbstractValidator<CreateReviewRequest>
{
    public CreateReviewRequestValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween(1, 5);
        RuleFor(x => x.Title).MaximumLength(150);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.OrderItemId).NotEmpty();
    }
}

public sealed class UpdateReviewRequestValidator : AbstractValidator<UpdateReviewRequest>
{
    public UpdateReviewRequestValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween(1, 5);
        RuleFor(x => x.Title).MaximumLength(150);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(4000);
    }
}

public sealed class ModerateReviewRequestValidator : AbstractValidator<ModerateReviewRequest>
{
    public ModerateReviewRequestValidator() => RuleFor(x => x.Note).MaximumLength(500);
}

public sealed class ReplyToReviewRequestValidator : AbstractValidator<ReplyToReviewRequest>
{
    public ReplyToReviewRequestValidator() => RuleFor(x => x.Body).NotEmpty().MaximumLength(2000);
}

// ---------------------------------------------------------------------------
// Coupons
// ---------------------------------------------------------------------------

public sealed class CreateCouponRequestValidator : AbstractValidator<CreateCouponRequest>
{
    public CreateCouponRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Matches("^[A-Za-z0-9_-]{3,32}$").WithMessage("Use 3-32 letters, digits, dashes or underscores.");
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.DiscountType).IsInEnum();
        RuleFor(x => x.DiscountValue)
            .InclusiveBetween(0.01m, 100m)
            .When(x => x.DiscountType == CouponDiscountType.Percentage)
            .WithMessage("A percentage discount must be between 0.01 and 100.");
        RuleFor(x => x.DiscountValue)
            .GreaterThan(0m)
            .When(x => x.DiscountType == CouponDiscountType.FixedAmount)
            .WithMessage("A fixed discount must be greater than zero.");
        RuleFor(x => x.MinimumOrderAmount).GreaterThan(0m).When(x => x.MinimumOrderAmount.HasValue);
        RuleFor(x => x.MaximumDiscountAmount).GreaterThan(0m).When(x => x.MaximumDiscountAmount.HasValue);
        RuleFor(x => x.UsageLimit).GreaterThan(0).When(x => x.UsageLimit.HasValue);
        RuleFor(x => x.PerUserLimit).InclusiveBetween(1, 1000);
        RuleFor(x => x.EndsAt).GreaterThan(x => x.StartsAt);
    }
}

public sealed class UpdateCouponRequestValidator : AbstractValidator<UpdateCouponRequest>
{
    public UpdateCouponRequestValidator()
    {
        RuleFor(x => x.DiscountValue).GreaterThan(0m);
        RuleFor(x => x.UsageLimit).GreaterThan(0).When(x => x.UsageLimit.HasValue);
        RuleFor(x => x.PerUserLimit).InclusiveBetween(1, 1000);
        RuleFor(x => x.EndsAt).GreaterThan(x => x.StartsAt);
    }
}

public sealed class ValidateCouponRequestValidator : AbstractValidator<ValidateCouponRequest>
{
    public ValidateCouponRequestValidator() => RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
}

// ---------------------------------------------------------------------------
// Query parameters
// ---------------------------------------------------------------------------

public sealed class ProductQueryValidator : AbstractValidator<ProductQuery>
{
    public ProductQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0).When(x => x.Page.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(x => x.PageSize.HasValue);
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.MinPrice).GreaterThanOrEqualTo(0m).When(x => x.MinPrice.HasValue);
        RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(0m).When(x => x.MaxPrice.HasValue);
        RuleFor(x => x.MaxPrice)
            .GreaterThanOrEqualTo(x => x.MinPrice ?? 0m)
            .WithMessage("maxPrice must be greater than or equal to minPrice.")
            .When(x => x.MaxPrice.HasValue && x.MinPrice.HasValue);
        RuleFor(x => x.MinRating).InclusiveBetween(1m, 5m).When(x => x.MinRating.HasValue);
    }
}

public sealed class OrderListQueryValidator : AbstractValidator<OrderListQuery>
{
    public OrderListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0).When(x => x.Page.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(x => x.PageSize.HasValue);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.To.HasValue && x.From.HasValue);
    }
}

public sealed class InventoryListQueryValidator : AbstractValidator<InventoryListQuery>
{
    public InventoryListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0).When(x => x.Page.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(x => x.PageSize.HasValue);
        RuleFor(x => x.Search).MaximumLength(200);
    }
}

public sealed class AuditListQueryValidator : AbstractValidator<Modules.Notifications.Abstractions.AuditListQuery>
{
    public AuditListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0).When(x => x.Page.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(x => x.PageSize.HasValue);
        RuleFor(x => x.Action).IsInEnum().When(x => x.Action.HasValue);
        RuleFor(x => x.Search).MaximumLength(200);
    }
}
