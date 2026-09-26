using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Auth.Abstractions;
using Marketplace.Application.Modules.Inventory.Abstractions;
using Marketplace.Application.Modules.Orders.Abstractions;
using Marketplace.Application.Modules.Orders.DTOs;
using Marketplace.Domain.Cart;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Marketplace.Domain.Commissions;
using Marketplace.Domain.Coupons;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Events;
using Marketplace.Domain.Identity;
using Marketplace.Domain.Inventory;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Sellers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CartEntity = Marketplace.Domain.Cart.Cart;

namespace Marketplace.Application.Modules.Orders.Services;

/// <summary>
/// Checkout. This is the transactional heart of the marketplace:
/// validate → price → reserve stock → create one order → split per seller →
/// create payment → clear the cart, all or nothing.
/// </summary>
public sealed class CheckoutService(
    IRepository<CartEntity> carts,
    IRepository<Domain.Catalog.Product> products,
    IRepository<ProductVariant> variants,
    IRepository<Coupon> coupons,
    IRepository<CouponUsage> couponUsages,
    IRepository<UserAddress> addresses,
    IRepository<Seller> sellers,
    IRepository<Domain.Orders.Order> orders,
    IRepository<Payment> payments,
    IRepository<InventoryReservation> reservations,
    IInventoryService inventory,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    IClock clock,
    IAuditService auditService,
    INotificationService notificationService,
    IRealtimeNotifier realtime,
    IPaymentGatewayResolver gateways,
    IOptions<MarketplaceOptions> options,
    ILogger<CheckoutService> logger) : ICheckoutService
{
    private readonly MarketplaceOptions _options = options.Value;

    public async Task<Result<QuoteResponse>> QuoteAsync(QuoteRequest request, CancellationToken cancellationToken = default)
    {
        var cart = await LoadCartAsync(cancellationToken).ConfigureAwait(false);
        if (cart is null || cart.Items.Count(i => !i.SavedForLater) == 0)
        {
            return Result<QuoteResponse>.Failure("Your cart is empty.");
        }

        var lines = await BuildPricedLinesAsync(cart, cancellationToken).ConfigureAwait(false);
        var (discountBySeller, couponSummary) = await ResolveCouponAsync(cart, request.CouponCode, cancellationToken).ConfigureAwait(false);

        var totals = OrderPricingCalculator.Calculate(
            lines,
            discountBySeller,
            _options.StandardShippingCost,
            _options.FreeShippingThreshold,
            _options.TaxRate);

        var rates = await SellerRatesAsync(lines, cancellationToken).ConfigureAwait(false);

        var sellerBreakdown = totals.SellerBreakdown.Select(s =>
        {
            var rate = rates.GetValueOrDefault(s.SellerId, _options.CommissionRate);
            var net = decimal.Round(s.Subtotal - totals.DiscountBySeller.GetValueOrDefault(s.SellerId), 2);
            var commission = Commission.CalculateCommission(net, rate);

            return new QuoteSellerLineResponse(
                s.SellerId,
                string.Empty,
                s.Subtotal,
                totals.DiscountBySeller.GetValueOrDefault(s.SellerId),
                ShippingFor(net, totals, s.SellerId),
                net,
                rate,
                decimal.Round(net - commission, 2),
                s.ItemCount);
        }).ToList();
        return Result<QuoteResponse>.Success(new QuoteResponse(
            totals.Subtotal,
            totals.DiscountAmount,
            totals.ShippingAmount,
            totals.TaxAmount,
            totals.TotalAmount,
            _options.Currency,
            lines.Sum(l => l.Quantity),
            sellerBreakdown,
            couponSummary,
            null));
    }

    public async Task<Result<CheckoutResponse>> CheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Result<CheckoutResponse>.Failure("Sign in to complete your order.");
        }

        // An Idempotency-Key makes a client retry safe: the same key returns the same order.
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await orders.Query().AsNoTracking()
                .FirstOrDefaultAsync(o => o.CustomerId == currentUser.UserId && o.CouponCode == request.IdempotencyKey, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                return Result<CheckoutResponse>.Success(new CheckoutResponse(
                    existing.Id, existing.OrderNumber, existing.TotalAmount, existing.Currency,
                    existing.Status, existing.SellerOrders.Count, request.PaymentMethod, null, false,
                    "This order was already placed."));
            }
        }

        var cart = await LoadTrackedCartAsync(cancellationToken).ConfigureAwait(false);
        if (cart is null || cart.Items.Count(i => !i.SavedForLater) == 0)
        {
            return Result<CheckoutResponse>.Failure("Your cart is empty.");
        }

        var address = await addresses.Query().AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.ShippingAddressId && a.UserId == currentUser.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (address is null)
        {
            return Result<CheckoutResponse>.Failure("Select a delivery address that belongs to your account.");
        }

        var lines = await BuildPricedLinesAsync(cart, cancellationToken).ConfigureAwait(false);
        if (lines.Count == 0)
        {
            return Result<CheckoutResponse>.Failure("Your cart is empty.");
        }

        var (discountBySeller, _) = await ResolveCouponAsync(cart, request.CouponCode, cancellationToken).ConfigureAwait(false);
        var totals = OrderPricingCalculator.Calculate(
            lines,
            discountBySeller,
            _options.StandardShippingCost,
            _options.FreeShippingThreshold,
            _options.TaxRate);

        var now = clock.UtcNow;
        var provider = ParseProvider(request.PaymentMethod);
        var order = Domain.Orders.Order.Place(
            currentUser.UserId,
            address,
            null,
            string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey,
            totals.Subtotal,
            totals.DiscountAmount,
            totals.ShippingAmount,
            totals.TaxAmount,
            _options.Currency,
            request.PaymentMethod,
            request.CustomerNote,
            now);

        await orders.AddAsync(order, cancellationToken).ConfigureAwait(false);

        // ---- 1. reserve stock (conditional, concurrency-safe) --------------------
        var createdReservations = new List<InventoryReservation>();
        foreach (var line in lines)
        {
            var reservation = await inventory.ReserveAsync(line.ProductVariantId, line.Quantity, order.Id, cancellationToken).ConfigureAwait(false);
            if (reservation.IsFailure)
            {
                // Undo the partial reservations already taken in this request.
                foreach (var taken in createdReservations)
                {
                    await inventory.ReleaseReservationAsync(taken, "checkout-aborted", cancellationToken).ConfigureAwait(false);
                }

                return Result<CheckoutResponse>.Failure(reservation.Value?.FailureReason ?? "An item is no longer available in the requested quantity.");
            }

            createdReservations.Add(await LoadReservationAsync(line.ProductVariantId, order.Id, cancellationToken).ConfigureAwait(false)
                ?? throw new BusinessRuleException("The reservation could not be persisted."));
        }

        // ---- 2. split into seller sub-orders -------------------------------------
        var rates = await SellerRatesAsync(lines, cancellationToken).ConfigureAwait(false);
        var sellerGroups = lines.GroupBy(l => l.SellerId).ToList();
        var sellerIds = new List<Guid>();

        for (var index = 0; index < sellerGroups.Count; index++)
        {
            var group = sellerGroups[index];
            var sellerId = group.Key;
            sellerIds.Add(sellerId);

            var sellerSubtotal = decimal.Round(group.Sum(l => l.LineTotal), 2);
            var sellerDiscount = totals.DiscountBySeller.GetValueOrDefault(sellerId);
            var sellerShipping = ShippingFor(sellerSubtotal - sellerDiscount, totals, sellerId);
            var sellerTax = decimal.Round((sellerSubtotal - sellerDiscount) * _options.TaxRate / 100m, 2);

            var sellerOrder = SellerOrder.Create(
                order.Id,
                sellerId,
                SellerOrder.GenerateSellerOrderNumber(order.OrderNumber, index),
                sellerSubtotal,
                sellerDiscount,
                sellerShipping,
                sellerTax,
                rates.GetValueOrDefault(sellerId, _options.CommissionRate),
                now);

            order.AddSellerOrder(sellerOrder);
        }

        // ---- 3. line items with snapshotted purchase data -------------------------
        var variantIds = lines.Select(l => l.ProductVariantId).ToList();
        var productRows = await products.Query().AsNoTracking()
            .Where(p => lines.Select(l => l.ProductId).Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.CategoryId,
                Url = p.Images.Where(i => i.IsPrimary).Select(i => i.Url).FirstOrDefault() ?? p.Images.Select(i => i.Url).FirstOrDefault()
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var productMap = productRows.ToDictionary(p => p.Id);
        var variantMap = await variants.Query().AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, cancellationToken)
            .ConfigureAwait(false);

        var remainingDiscount = new Dictionary<Guid, decimal>(totals.DiscountBySeller);
        var soldCounts = new Dictionary<Guid, int>();

        foreach (var line in lines)
        {
            var sellerOrder = order.SellerOrders.First(so => so.SellerId == line.SellerId);
            var product = productMap.GetValueOrDefault(line.ProductId);
            var variant = variantMap.GetValueOrDefault(line.ProductVariantId);

            var item = OrderItem.Create(
                order.Id,
                sellerOrder.Id,
                line.ProductId,
                line.ProductVariantId,
                line.SellerId,
                product?.CategoryId ?? Guid.Empty,
                product?.Name ?? "Product",
                product?.Url,
                variant?.Name ?? "Default",
                variant?.Sku ?? string.Empty,
                line.Quantity,
                line.UnitPrice,
                line.LineTotal,
                now);

            // Push the seller's discount down onto its lines, largest line first.
            var sellerDiscountLeft = remainingDiscount.GetValueOrDefault(line.SellerId);
            var applied = Math.Min(sellerDiscountLeft, item.LineTotal);
            item.AllocateDiscount(applied, now);
            remainingDiscount[line.SellerId] = sellerDiscountLeft - applied;

            order.AddItem(item);
            sellerOrder.AddItem(item);

            soldCounts[line.ProductId] = soldCounts.GetValueOrDefault(line.ProductId) + line.Quantity;
        }

        foreach (var (productId, quantity) in soldCounts)
        {
            var product = await products.Query().FirstOrDefaultAsync(p => p.Id == productId, cancellationToken).ConfigureAwait(false);
            product?.RecordSale(quantity, now);
        }

        // ---- 4. payment ----------------------------------------------------------
        var payment = Payment.Create(order.Id, currentUser.UserId, provider, totals.TotalAmount, _options.Currency, request.IdempotencyKey, now);
        await payments.AddAsync(payment, cancellationToken).ConfigureAwait(false);

        var gateway = gateways.Resolve(provider);
        var initiation = await gateway.CreatePaymentAsync(new PaymentInitiationRequest(
            payment.Id,
            order.Id,
            order.OrderNumber,
            totals.TotalAmount,
            _options.Currency,
            currentUser.Email ?? string.Empty,
            $"{currentUser.UserId}",
            $"{_options.FrontendBaseUrl}/checkout?order={order.OrderNumber}",
            $"{_options.FrontendBaseUrl}/checkout?failed={order.OrderNumber}",
            $"{_options.FrontendBaseUrl}/cart",
            null), cancellationToken).ConfigureAwait(false);

        if (initiation.IsSuccess)
        {
            payment.AttachGatewayResponse(
                initiation.GatewayPaymentId,
                initiation.RedirectUrl,
                PaymentTransaction.Create(payment.Id, PaymentTransactionType.GatewayResponse, null, initiation.RawResponse, true, null, now),
                now);

            if (provider == PaymentProvider.CashOnDelivery)
            {
                order.MarkPaid(now);
            }
        }
        else
        {
            payment.MarkFailed(
                initiation.FailureReason ?? "The payment could not be started.",
                PaymentTransaction.Create(payment.Id, PaymentTransactionType.GatewayResponse, null, initiation.RawResponse, false, initiation.FailureReason, now),
                now);

            foreach (var reservation in createdReservations)
            {
                await inventory.ReleaseReservationAsync(reservation, "payment-initiation-failed", cancellationToken).ConfigureAwait(false);
            }

            return Result<CheckoutResponse>.Failure(initiation.FailureReason ?? "The payment could not be started.");
        }

        // ---- 5. clear the cart ---------------------------------------------------
        cart.Clear(now);

        order.AddDomainEvent(new OrderCreatedEvent(order.Id, order.OrderNumber, order.CustomerId, sellerIds.Count, order.TotalAmount, now));

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditService.RecordAsync(AuditAction.OrderCreated, nameof(Domain.Orders.Order), order.Id, order.OrderNumber,
            new { order.OrderNumber, order.TotalAmount, Sellers = sellerIds.Count }, cancellationToken).ConfigureAwait(false);

        await realtime.OrderCreatedAsync(order.Id, order.OrderNumber, order.CustomerId, sellerIds, order.TotalAmount, cancellationToken).ConfigureAwait(false);

        foreach (var sellerId in sellerIds)
        {
            var seller = await sellers.GetByIdAsync(sellerId, cancellationToken).ConfigureAwait(false);
            if (seller is not null)
            {
                await notificationService.NotifySellerAsync(seller.UserId, NotificationType.OrderCreated,
                    "New order received",
                    $"Order {order.OrderNumber} is waiting for you to confirm.",
                    "/seller/orders", cancellationToken).ConfigureAwait(false);
            }
        }

        logger.LogInformation("Order {OrderNumber} placed by user {UserId} for {Total}", order.OrderNumber, order.CustomerId, order.TotalAmount);

        return Result<CheckoutResponse>.Success(new CheckoutResponse(
            order.Id,
            order.OrderNumber,
            order.TotalAmount,
            order.Currency,
            order.Status,
            sellerIds.Count,
            request.PaymentMethod,
            initiation.RedirectUrl,
            gateway.RequiresCustomerAction,
            null));
    }

    private static decimal ShippingFor(decimal sellerNet, OrderTotals totals, Guid sellerId)
    {
        if (totals.ShippingAmount <= 0m)
        {
            return 0m;
        }

        var shippingSellerCount = totals.SellerBreakdown.Count(s => s.Subtotal - totals.DiscountBySeller.GetValueOrDefault(s.SellerId) > 0m);
        return shippingSellerCount <= 0 ? 0m : decimal.Round(totals.ShippingAmount / shippingSellerCount, 2, MidpointRounding.AwayFromZero);
    }

    private static PaymentProvider ParseProvider(string paymentMethod) =>
        Enum.TryParse<PaymentProvider>(paymentMethod, ignoreCase: true, out var provider) ? provider : PaymentProvider.Mock;

    private Task<CartEntity?> LoadCartAsync(CancellationToken cancellationToken) =>
        carts.Query().Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == currentUser.UserId, cancellationToken);

    private Task<CartEntity?> LoadTrackedCartAsync(CancellationToken cancellationToken) =>
        carts.Query().Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == currentUser.UserId, cancellationToken);

    private Task<InventoryReservation?> LoadReservationAsync(Guid variantId, Guid orderId, CancellationToken cancellationToken) =>
        reservations.Query()
            .Where(r => r.OrderId == orderId && r.Quantity > 0)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<IReadOnlyList<PricedLine>> BuildPricedLinesAsync(CartEntity cart, CancellationToken cancellationToken)
    {
        var items = cart.Items.Where(i => !i.SavedForLater).ToList();
        if (items.Count == 0)
        {
            return [];
        }

        var variantIds = items.Select(i => i.ProductVariantId).ToList();
        var variantRows = await variants.Query().AsNoTracking()
            .Where(v => variantIds.Contains(v.Id) && v.IsActive)
            .Select(v => new { v.Id, v.ProductId, v.Price, v.Sku })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var productIds = variantRows.Select(v => v.ProductId).ToList();
        var productRows = await products.Query().AsNoTracking()
            .Where(p => productIds.Contains(p.Id) && p.Status == ProductStatus.Published && !p.IsDeleted)
            .Select(p => new { p.Id, p.SellerId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var productMap = productRows.ToDictionary(p => p.Id);
        var variantMap = variantRows.ToDictionary(v => v.Id);

        var lines = new List<PricedLine>();
        foreach (var item in items)
        {
            if (!variantMap.TryGetValue(item.ProductVariantId, out var variant) ||
                !productMap.TryGetValue(variant.ProductId, out var product))
            {
                continue;
            }

            // The unit price always comes from the catalogue, never from the cart row.
            lines.Add(new PricedLine(product.Id, variant.Id, product.SellerId, variant.Price, item.Quantity));
        }

        return lines;
    }

    private async Task<(Dictionary<Guid, decimal>? Discounts, CouponValidationSummaryResponse? Summary)> ResolveCouponAsync(
        CartEntity cart,
        string? couponCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(couponCode))
        {
            return (null, null);
        }

        var code = couponCode.Trim().ToUpperInvariant();
        var coupon = await coupons.Query().AsNoTracking().FirstOrDefaultAsync(c => c.Code == code, cancellationToken).ConfigureAwait(false);
        if (coupon is null)
        {
            return (null, null);
        }

        var lines = await BuildPricedLinesAsync(cart, cancellationToken).ConfigureAwait(false);
        var productIds = lines.Select(l => l.ProductId).ToList();

        // A seller-scoped coupon only discounts that seller's lines.
        var eligibleLines = coupon.IsGlobal
            ? lines
            : lines.Where(l => l.SellerId == coupon.SellerId).ToList();

        if (eligibleLines.Count == 0)
        {
            return (null, null);
        }

        var eligibleSubtotal = decimal.Round(eligibleLines.Sum(l => l.LineTotal), 2);
        var usageCount = await couponUsages.Query().AsNoTracking()
            .CountAsync(u => u.CouponId == coupon.Id && u.UserId == currentUser.UserId, cancellationToken)
            .ConfigureAwait(false);

        var basketSellerIds = lines.Select(l => l.SellerId).Distinct().ToList();
        var result = CouponCalculator.Validate(
            coupon,
            eligibleSubtotal,
            usageCount,
            coupon.SellerId,
            basketSellerIds.Count == 1 ? basketSellerIds[0] : null,
            productIds,
            clock.UtcNow);

        if (!result.IsValid)
        {
            return (null, null);
        }

        var breakdown = eligibleLines
            .GroupBy(l => l.SellerId)
            .Select(g => new SellerSubtotal(g.Key, decimal.Round(g.Sum(l => l.LineTotal), 2), g.Sum(l => l.Quantity)))
            .ToList();

        var allocation = OrderPricingCalculator.AllocateDiscount(breakdown, result.DiscountAmount);

        var label = coupon.DiscountType == CouponDiscountType.Percentage
            ? $"{coupon.DiscountValue:0.##}% off"
            : $"{coupon.DiscountValue:0.00} off";

        return (allocation, new CouponValidationSummaryResponse(coupon.Code, result.DiscountAmount, label));
    }

    private async Task<Dictionary<Guid, decimal>> SellerRatesAsync(IReadOnlyCollection<PricedLine> lines, CancellationToken cancellationToken)
    {
        var sellerIds = lines.Select(l => l.SellerId).Distinct().ToList();
        var rates = await sellers.Query().AsNoTracking()
            .Where(s => sellerIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.DefaultCommissionRate, cancellationToken)
            .ConfigureAwait(false);

        return rates;
    }
}
