using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Auth.Abstractions;
using Marketplace.Application.Modules.Cart.Abstractions;
using Marketplace.Application.Modules.Orders.DTOs;
using Marketplace.Domain.Cart;
using CartEntity = Marketplace.Domain.Cart.Cart;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Sellers;
using InventoryRecord = Marketplace.Domain.Inventory.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marketplace.Application.Modules.Cart.Services;

/// <summary>
/// Shopping cart. Items are grouped by seller in the response so the UI can present one
/// section per store, and unit prices are refreshed from the catalogue on every read so a
/// price change is never hidden from the customer.
/// </summary>
public sealed class CartService(
    IRepository<CartEntity> carts,
    IRepository<Domain.Catalog.Product> products,
    IRepository<ProductVariant> variants,
    IRepository<SellerStore> stores,
    IRepository<InventoryRecord> inventories,
    IRepository<Seller> sellers,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<MarketplaceOptions> options) : ICartService
{
    private readonly MarketplaceOptions _options = options.Value;

    public async Task<Result<CartResponse>> GetAsync(string? guestToken, CancellationToken cancellationToken = default)
    {
        var cart = await ResolveCartAsync(guestToken, createIfMissing: false, cancellationToken).ConfigureAwait(false);
        return cart is null
            ? Result<CartResponse>.Success(EmptyCart())
            : Result<CartResponse>.Success(await BuildAsync(cart, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<CartResponse>> AddItemAsync(AddCartItemRequest request, string? guestToken, CancellationToken cancellationToken = default)
    {
        var product = await products.Query().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.Status == ProductStatus.Published && !p.IsDeleted, cancellationToken)
            .ConfigureAwait(false);

        if (product is null)
        {
            return Result<CartResponse>.Failure("This product is not available.");
        }

        var variant = product.Variants.FirstOrDefault(v => v.Id == request.ProductVariantId && v.IsActive);
        if (variant is null)
        {
            return Result<CartResponse>.Failure("Please choose an available option.");
        }

        var sellable = await inventories.Query().AsNoTracking()
            .Where(i => i.ProductVariantId == variant.Id)
            .Select(i => (int?)(i.AvailableQuantity - i.ReservedQuantity))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0;

        if (sellable <= 0)
        {
            return Result<CartResponse>.Failure("This item is currently out of stock.");
        }

        var cart = await ResolveCartAsync(guestToken, createIfMissing: true, cancellationToken).ConfigureAwait(false);
        if (cart is null)
        {
            return Result<CartResponse>.Failure("Unable to create a cart for this session.");
        }

        var existing = cart.Items.FirstOrDefault(i => i.ProductVariantId == variant.Id);
        var desired = (existing?.Quantity ?? 0) + request.Quantity;

        if (desired > sellable)
        {
            return Result<CartResponse>.Failure($"Only {sellable} unit(s) are available.");
        }

        var tracked = await LoadTrackedCartAsync(cart.Id, cancellationToken).ConfigureAwait(false);
        if (tracked is null)
        {
            return Result<CartResponse>.Failure("Unable to update the cart.");
        }

        tracked.AddItem(product, variant, request.Quantity, variant.Price, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<CartResponse>.Success(await BuildAsync(tracked, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<CartResponse>> UpdateItemAsync(Guid itemId, UpdateCartItemRequest request, string? guestToken, CancellationToken cancellationToken = default)
    {
        var cart = await ResolveCartAsync(guestToken, createIfMissing: false, cancellationToken).ConfigureAwait(false);
        if (cart is null)
        {
            return Result<CartResponse>.Failure("Cart not found.");
        }

        var tracked = await LoadTrackedCartAsync(cart.Id, cancellationToken).ConfigureAwait(false);
        if (tracked is null)
        {
            return Result<CartResponse>.Failure("Cart not found.");
        }

        var item = tracked.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
        {
            return Result<CartResponse>.Failure("Item not found in this cart.");
        }

        var sellable = await inventories.Query().AsNoTracking()
            .Where(i => i.ProductVariantId == item.ProductVariantId)
            .Select(i => (int?)(i.AvailableQuantity - i.ReservedQuantity))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0;

        if (request.Quantity > sellable)
        {
            return Result<CartResponse>.Failure($"Only {sellable} unit(s) are available.");
        }

        tracked.UpdateQuantity(itemId, request.Quantity, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<CartResponse>.Success(await BuildAsync(tracked, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<CartResponse>> RemoveItemAsync(Guid itemId, string? guestToken, CancellationToken cancellationToken = default)
    {
        var cart = await ResolveCartAsync(guestToken, createIfMissing: false, cancellationToken).ConfigureAwait(false);
        if (cart is null)
        {
            return Result<CartResponse>.Failure("Cart not found.");
        }

        var tracked = await LoadTrackedCartAsync(cart.Id, cancellationToken).ConfigureAwait(false);
        if (tracked is null)
        {
            return Result<CartResponse>.Failure("Cart not found.");
        }

        tracked.RemoveItem(itemId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<CartResponse>.Success(await BuildAsync(tracked, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<CartResponse>> ToggleSavedForLaterAsync(Guid itemId, string? guestToken, CancellationToken cancellationToken = default)
    {
        var cart = await ResolveCartAsync(guestToken, createIfMissing: false, cancellationToken).ConfigureAwait(false);
        if (cart is null)
        {
            return Result<CartResponse>.Failure("Cart not found.");
        }

        var tracked = await LoadTrackedCartAsync(cart.Id, cancellationToken).ConfigureAwait(false);
        if (tracked is null)
        {
            return Result<CartResponse>.Failure("Cart not found.");
        }

        tracked.ToggleSavedForLater(itemId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<CartResponse>.Success(await BuildAsync(tracked, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<CartResponse>> ClearAsync(string? guestToken, CancellationToken cancellationToken = default)
    {
        var cart = await ResolveCartAsync(guestToken, createIfMissing: false, cancellationToken).ConfigureAwait(false);
        if (cart is null)
        {
            return Result<CartResponse>.Success(EmptyCart());
        }

        var tracked = await LoadTrackedCartAsync(cart.Id, cancellationToken).ConfigureAwait(false);
        if (tracked is null)
        {
            return Result<CartResponse>.Success(EmptyCart());
        }

        tracked.Clear(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<CartResponse>.Success(await BuildAsync(tracked, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<CartResponse>> MergeGuestCartAsync(string guestToken, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Result<CartResponse>.Failure("Sign in to merge your cart.");
        }

        var guestCart = await carts.Query()
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.GuestToken == guestToken, cancellationToken)
            .ConfigureAwait(false);

        if (guestCart is null || guestCart.Items.Count == 0)
        {
            return await GetAsync(null, cancellationToken).ConfigureAwait(false);
        }

        var userCart = await LoadTrackedCartAsync(
            (await EnsureUserCartIdAsync(cancellationToken).ConfigureAwait(false)),
            cancellationToken).ConfigureAwait(false);

        if (userCart is null)
        {
            return Result<CartResponse>.Failure("Unable to prepare your cart.");
        }

        foreach (var guestItem in guestCart.Items.Where(i => !i.SavedForLater))
        {
            var product = await products.Query().AsNoTracking().FirstOrDefaultAsync(p => p.Id == guestItem.ProductId, cancellationToken).ConfigureAwait(false);
            var variant = product?.Variants.FirstOrDefault(v => v.Id == guestItem.ProductVariantId);
            if (product is null || variant is null)
            {
                continue;
            }

            var existing = userCart.Items.FirstOrDefault(i => i.ProductVariantId == variant.Id);
            if (existing is not null)
            {
                userCart.UpdateQuantity(existing.Id, existing.Quantity + guestItem.Quantity, clock.UtcNow);
            }
            else
            {
                userCart.AddItem(product, variant, guestItem.Quantity, variant.Price, clock.UtcNow);
            }
        }

        guestCart.Clear(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<CartResponse>.Success(await BuildAsync(userCart, cancellationToken).ConfigureAwait(false));
    }

    private async Task<Guid> EnsureUserCartIdAsync(CancellationToken cancellationToken)
    {
        var existing = await carts.Query().AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == currentUser.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing.Id;
        }

        var cart = CartEntity.ForCustomer(currentUser.UserId, clock.UtcNow);
        await carts.AddAsync(cart, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return cart.Id;
    }

    private async Task<CartEntity?> ResolveCartAsync(string? guestToken, bool createIfMissing, CancellationToken cancellationToken)
    {
        if (currentUser.IsAuthenticated)
        {
            var existing = await carts.Query().AsNoTracking()
                .FirstOrDefaultAsync(c => c.UserId == currentUser.UserId, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                return existing;
            }

            if (!createIfMissing)
            {
                return null;
            }

            var created = CartEntity.ForCustomer(currentUser.UserId, clock.UtcNow);
            await carts.AddAsync(created, cancellationToken).ConfigureAwait(false);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return created;
        }

        if (string.IsNullOrWhiteSpace(guestToken))
        {
            return null;
        }

        var guestCart = await carts.Query().AsNoTracking()
            .FirstOrDefaultAsync(c => c.GuestToken == guestToken, cancellationToken)
            .ConfigureAwait(false);

        if (guestCart is not null || !createIfMissing)
        {
            return guestCart;
        }

        var newCart = CartEntity.ForGuest(guestToken, clock.UtcNow);
        await carts.AddAsync(newCart, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return newCart;
    }

    private Task<CartEntity?> LoadTrackedCartAsync(Guid cartId, CancellationToken cancellationToken) =>
        carts.Query()
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == cartId, cancellationToken);

    private async Task<CartResponse> BuildAsync(CartEntity cart, CancellationToken cancellationToken)
    {
        var activeItems = cart.Items.Where(i => !i.SavedForLater).ToList();
        if (activeItems.Count == 0)
        {
            return new CartResponse(cart.Id, 0, 0, 0m, 0m, 0m, _options.Currency, [], cart.LastActivityAt);
        }

        var variantIds = activeItems.Select(i => i.ProductVariantId).ToList();
        var productIds = activeItems.Select(i => i.ProductId).ToList();
        var sellerIds = activeItems.Select(i => i.SellerId).Distinct().ToList();

        var variantInfo = await variants.Query().AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, cancellationToken)
            .ConfigureAwait(false);

        var productInfo = await products.Query().AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.SlugValue, Url = p.Images.Where(i => i.IsPrimary).Select(i => i.Url).FirstOrDefault() ?? p.Images.Select(i => i.Url).FirstOrDefault() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var productMap = productInfo.ToDictionary(p => p.Id);

        var stockRows = await inventories.Query().AsNoTracking()
            .Where(i => variantIds.Contains(i.ProductVariantId))
            .Select(i => new { i.ProductVariantId, i.AvailableQuantity, i.ReservedQuantity })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var stock = stockRows.ToDictionary(r => r.ProductVariantId, r => r.AvailableQuantity - r.ReservedQuantity);

        var storeList = await stores.Query().AsNoTracking()
            .Where(s => sellerIds.Contains(s.SellerId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var sellerList = await sellers.Query().AsNoTracking()
            .Where(s => sellerIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.BusinessName, cancellationToken)
            .ConfigureAwait(false);

        var groups = new List<CartSellerGroupResponse>();

        foreach (var group in activeItems.GroupBy(i => i.SellerId))
        {
            var store = storeList.FirstOrDefault(s => s.SellerId == group.Key);
            var items = new List<CartItemResponse>();
            decimal groupSubtotal = 0m;

            foreach (var item in group)
            {
                var variant = variantInfo.GetValueOrDefault(item.ProductVariantId);
                var product = productMap.GetValueOrDefault(item.ProductId);
                var currentPrice = variant?.Price ?? item.UnitPrice;
                var available = stock.GetValueOrDefault(item.ProductVariantId);
                var priceChanged = Math.Round(currentPrice, 2) != Math.Round(item.UnitPrice, 2);
                var inStock = available > 0;

                var lineTotal = decimal.Round(currentPrice * item.Quantity, 2);
                groupSubtotal += lineTotal;

                items.Add(new CartItemResponse(
                    item.Id,
                    item.ProductId,
                    item.ProductVariantId,
                    product?.Name ?? "Unavailable product",
                    product?.SlugValue ?? string.Empty,
                    product?.Url,
                    variant?.Name ?? string.Empty,
                    variant?.Sku ?? string.Empty,
                    item.Quantity,
                    available,
                    currentPrice,
                    lineTotal,
                    item.SavedForLater,
                    priceChanged,
                    inStock,
                    priceChanged ? "The price changed since this item was added." : null));
            }

            // Saved-for-later items are surfaced but never priced into the totals.
            var savedItems = cart.Items
                .Where(i => i.SavedForLater && i.SellerId == group.Key)
                .Select(i => new CartItemResponse(
                    i.Id, i.ProductId, i.ProductVariantId,
                    productMap.GetValueOrDefault(i.ProductId)?.Name ?? "Unavailable product",
                    productMap.GetValueOrDefault(i.ProductId)?.SlugValue ?? string.Empty,
                    productMap.GetValueOrDefault(i.ProductId)?.Url,
                    variantInfo.GetValueOrDefault(i.ProductVariantId)?.Name ?? string.Empty,
                    variantInfo.GetValueOrDefault(i.ProductVariantId)?.Sku ?? string.Empty,
                    i.Quantity, stock.GetValueOrDefault(i.ProductVariantId), i.UnitPrice, i.LineTotal, true, false, true, null))
                .ToList();

            items.AddRange(savedItems);

            groups.Add(new CartSellerGroupResponse(
                group.Key,
                sellerList.GetValueOrDefault(group.Key, string.Empty),
                store?.Name ?? string.Empty,
                store?.SlugValue ?? string.Empty,
                store?.LogoUrl,
                decimal.Round(groupSubtotal, 2),
                items));
        }

        var subtotal = decimal.Round(groups.Sum(g => g.Subtotal), 2);
        var shipping = subtotal >= _options.FreeShippingThreshold
            ? 0m
            : decimal.Round(groups.Count * _options.StandardShippingCost, 2);

        return new CartResponse(
            cart.Id,
            activeItems.Count,
            activeItems.Sum(i => i.Quantity),
            subtotal,
            shipping,
            decimal.Round(subtotal + shipping, 2),
            _options.Currency,
            groups,
            cart.LastActivityAt);
    }

    private static CartResponse EmptyCart() =>
        new(Guid.Empty, 0, 0, 0m, 0m, 0m, "USD", [], DateTimeOffset.UtcNow);
}
