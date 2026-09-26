using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Cart.Abstractions;
using Marketplace.Application.Modules.Sellers.DTOs;
using Marketplace.Domain.Cart;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Sellers;
using Microsoft.EntityFrameworkCore;
using InventoryRecord = Marketplace.Domain.Inventory.Inventory;
using WishlistEntity = Marketplace.Domain.Cart.Wishlist;

namespace Marketplace.Application.Modules.Cart.Services;

/// <summary>Saved products, kept as its own aggregate so it never couples to the cart.</summary>
public sealed class WishlistService(
    IRepository<WishlistEntity> wishlists,
    IRepository<Domain.Catalog.Product> products,
    IRepository<SellerStore> stores,
    IRepository<InventoryRecord> inventories,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    IClock clock) : IWishlistService
{
    public async Task<Result<IReadOnlyList<WishlistItemResponse>>> GetAsync(CancellationToken cancellationToken = default)
    {
        var wishlist = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (wishlist is null || wishlist.Items.Count == 0)
        {
            return Result<IReadOnlyList<WishlistItemResponse>>.Success([]);
        }

        var productIds = wishlist.Items.Select(i => i.ProductId).ToList();
        var addedAt = wishlist.Items.ToDictionary(i => i.ProductId, i => i.AddedAt);

        var productRows = await products.Query().AsNoTracking()
            .Where(p => productIds.Contains(p.Id) && p.Status == ProductStatus.Published && !p.IsDeleted)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.SlugValue,
                p.BasePrice,
                p.CompareAtPrice,
                p.RatingAverage,
                p.RatingCount,
                p.SellerId,
                Url = p.Images.Where(i => i.IsPrimary).Select(i => i.Url).FirstOrDefault() ?? p.Images.Select(i => i.Url).FirstOrDefault()
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var sellerIds = productRows.Select(p => p.SellerId).Distinct().ToList();
        var storeList = await stores.Query().AsNoTracking()
            .Where(s => sellerIds.Contains(s.SellerId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var stockRows = await inventories.Query().AsNoTracking()
            .Where(i => productIds.Contains(i.ProductId))
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, Sellable = g.Sum(i => i.AvailableQuantity - i.ReservedQuantity) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var stock = stockRows.ToDictionary(r => r.ProductId, r => r.Sellable);

        var result = productRows
            .Select(p =>
            {
                var store = storeList.FirstOrDefault(s => s.SellerId == p.SellerId);
                var discount = p.CompareAtPrice is { } compare && compare > p.BasePrice
                    ? (int)Math.Round((compare - p.BasePrice) / compare * 100m, MidpointRounding.AwayFromZero)
                    : 0;

                return new WishlistItemResponse(
                    p.Id, p.Name, p.SlugValue, p.BasePrice, p.CompareAtPrice, discount, p.Url,
                    p.RatingAverage, p.RatingCount, stock.GetValueOrDefault(p.Id) > 0,
                    store?.Name ?? string.Empty, store?.SlugValue ?? string.Empty,
                    addedAt.GetValueOrDefault(p.Id, DateTimeOffset.UtcNow));
            })
            .OrderByDescending(i => i.AddedAt)
            .ToList();

        return Result<IReadOnlyList<WishlistItemResponse>>.Success(result);
    }

    public async Task<Result<IReadOnlyList<WishlistItemResponse>>> AddAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var productExists = await products.AnyAsync(p => p.Id == productId && !p.IsDeleted, cancellationToken).ConfigureAwait(false);
        if (!productExists)
        {
            return Result<IReadOnlyList<WishlistItemResponse>>.Failure("Product not found.", ResultErrorCodes.NotFound);
        }

        var wishlist = await LoadTrackedAsync(cancellationToken).ConfigureAwait(false);
        if (wishlist is null)
        {
            return Result<IReadOnlyList<WishlistItemResponse>>.Failure("Unable to create the wishlist.");
        }

        wishlist.Add(productId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await GetAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result> RemoveAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var wishlist = await LoadTrackedAsync(cancellationToken).ConfigureAwait(false);
        if (wishlist is null)
        {
            return Result.Failure("Wishlist not found.", ResultErrorCodes.NotFound);
        }

        wishlist.Remove(productId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    public async Task<Result> ClearAsync(CancellationToken cancellationToken = default)
    {
        var wishlist = await LoadTrackedAsync(cancellationToken).ConfigureAwait(false);
        if (wishlist is null)
        {
            return Result.Success();
        }

        wishlist.Clear();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    private Task<WishlistEntity?> LoadAsync(CancellationToken cancellationToken) =>
        wishlists.Query()
            .Include(w => w.Items)
            .FirstOrDefaultAsync(w => w.UserId == currentUser.UserId, cancellationToken);

    private async Task<WishlistEntity?> LoadTrackedAsync(CancellationToken cancellationToken)
    {
        var existing = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var created = WishlistEntity.ForUser(currentUser.UserId, clock.UtcNow);
        await wishlists.AddAsync(created, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return created;
    }
}
