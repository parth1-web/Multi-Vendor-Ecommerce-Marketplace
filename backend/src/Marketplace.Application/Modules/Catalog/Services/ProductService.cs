using Marketplace.Application.Common.Extensions;
using Marketplace.Application.Common.Helpers;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Catalog.Abstractions;
using Marketplace.Application.Modules.Catalog.DTOs;
using Marketplace.Application.Modules.Reviews.DTOs;
using Marketplace.Application.Modules.Sellers.DTOs;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Events;
using Marketplace.Domain.Inventory;
using Marketplace.Domain.Reviews;
using Marketplace.Domain.Sellers;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Modules.Catalog.Services;

/// <summary>
/// Product catalogue: public search and browsing plus seller-scoped and admin management.
/// Ownership is applied as a query predicate, never as a post-filter.
/// </summary>
public sealed class ProductService(
    IRepository<Product> products,
    IRepository<Category> categories,
    IRepository<Seller> sellers,
    IRepository<SellerStore> stores,
    IRepository<Inventory> inventories,
    IRepository<Review> reviews,
    ICurrentUser currentUser,
    ICacheService cache,
    IUnitOfWork unitOfWork,
    IClock clock,
    IAuditService auditService,
    INotificationService notificationService,
    SlugGenerator slugs) : IProductService
{
    private static readonly TimeSpan ListTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DetailTtl = TimeSpan.FromMinutes(15);

    public async Task<PagedResult<ProductSummaryResponse>> ListAsync(ProductQuery query, CancellationToken cancellationToken = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);

        var source = ApplyFilters(products.Query().AsNoTracking(), query);

        source = query.Sort switch
        {
            ProductSortOption.PriceAsc => source.OrderBy(p => p.BasePrice).ThenByDescending(p => p.CreatedAt),
            ProductSortOption.PriceDesc => source.OrderByDescending(p => p.BasePrice).ThenByDescending(p => p.CreatedAt),
            ProductSortOption.Rating => source.OrderByDescending(p => p.RatingAverage).ThenByDescending(p => p.RatingCount),
            ProductSortOption.Popular => source.OrderByDescending(p => p.SoldCount).ThenByDescending(p => p.ViewCount),
            ProductSortOption.NameAsc => source.OrderBy(p => p.Name),
            ProductSortOption.NameDesc => source.OrderByDescending(p => p.Name),
            ProductSortOption.Discount => source.OrderByDescending(p => p.CompareAtPrice.HasValue)
                .ThenByDescending(p => (p.CompareAtPrice ?? 0m) - p.BasePrice),
            _ => source.OrderByDescending(p => p.CreatedAt)
        };

        var result = await source.ToPagedResultAsync(page, p => Project(p, p.Images.FirstOrDefault(i => i.IsPrimary)?.Url ?? p.Images.FirstOrDefault()?.Url,
            p.Images.FirstOrDefault(i => i.IsPrimary)?.AltText, null, null, null, null, 0, 0), cancellationToken).ConfigureAwait(false);

        var hydrated = await HydrateAsync(result.Items.ToList(), cancellationToken).ConfigureAwait(false);
        return new Application.Common.Models.PagedResult<ProductSummaryResponse>(hydrated, result.Page, result.PageSize, result.TotalCount);
    }


    public async Task<IReadOnlyList<ProductSummaryResponse>> GetFeaturedAsync(int take, CancellationToken cancellationToken = default)
    {
        var items = await products.Query()
            .AsNoTracking()
            .Where(p => p.Status == ProductStatus.Published && !p.IsDeleted && p.IsFeatured)
            .OrderByDescending(p => p.SoldCount)
            .Take(Math.Clamp(take, 1, 40))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await HydrateAsync(items.Select(p => Project(p, p.Images.FirstOrDefault(i => i.IsPrimary)?.Url ?? p.Images.FirstOrDefault()?.Url,
            p.Images.FirstOrDefault(i => i.IsPrimary)?.AltText, null, null, null, null, 0, 0)).ToList(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProductSummaryResponse>> GetBestSellersAsync(int take, CancellationToken cancellationToken = default)
    {
        var items = await products.Query()
            .AsNoTracking()
            .Where(p => p.Status == ProductStatus.Published && !p.IsDeleted)
            .OrderByDescending(p => p.SoldCount)
            .ThenByDescending(p => p.RatingCount)
            .Take(Math.Clamp(take, 1, 40))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await HydrateAsync(items.Select(p => Project(p, p.Images.FirstOrDefault(i => i.IsPrimary)?.Url ?? p.Images.FirstOrDefault()?.Url,
            p.Images.FirstOrDefault(i => i.IsPrimary)?.AltText, null, null, null, null, 0, 0)).ToList(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProductSummaryResponse>> GetNewArrivalsAsync(int take, CancellationToken cancellationToken = default)
    {
        var items = await products.Query()
            .AsNoTracking()
            .Where(p => p.Status == ProductStatus.Published && !p.IsDeleted)
            .OrderByDescending(p => p.CreatedAt)
            .Take(Math.Clamp(take, 1, 40))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await HydrateAsync(items.Select(p => Project(p, p.Images.FirstOrDefault(i => i.IsPrimary)?.Url ?? p.Images.FirstOrDefault()?.Url,
            p.Images.FirstOrDefault(i => i.IsPrimary)?.AltText, null, null, null, null, 0, 0)).ToList(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ProductDetailResponse>> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var key = CacheKeys.ProductBySlug(slug.ToLowerInvariant());
        var cached = await cache.GetAsync<ProductDetailResponse>(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return Result<ProductDetailResponse>.Success(cached);
        }

        var product = await products.Query()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.SlugValue == slug.ToLowerInvariant() && !p.IsDeleted, cancellationToken)
            .ConfigureAwait(false);

        if (product is null)
        {
            return Result<ProductDetailResponse>.Failure("Product not found.");
        }

        var dto = await BuildDetailAsync(product, cancellationToken).ConfigureAwait(false);
        await cache.SetAsync(key, dto, DetailTtl, [CacheKeys.ProductTag(product.Id)], cancellationToken).ConfigureAwait(false);

        return Result<ProductDetailResponse>.Success(dto);
    }

    public async Task<Result<ProductDetailResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var key = CacheKeys.Product(id);
        var cached = await cache.GetAsync<ProductDetailResponse>(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return Result<ProductDetailResponse>.Success(cached);
        }

        var product = await products.Query().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return Result<ProductDetailResponse>.Failure("Product not found.");
        }

        var dto = await BuildDetailAsync(product, cancellationToken).ConfigureAwait(false);
        await cache.SetAsync(key, dto, DetailTtl, [CacheKeys.ProductTag(product.Id)], cancellationToken).ConfigureAwait(false);
        return Result<ProductDetailResponse>.Success(dto);
    }

    public async Task<Result<ProductSummaryResponse>> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default)
    {
        if (currentUser.SellerId is not { } sellerId)
        {
            return Result<ProductSummaryResponse>.Failure("Only sellers can create products.");
        }

        var seller = await sellers.GetByIdAsync(sellerId, cancellationToken).ConfigureAwait(false);
        if (seller is null || !seller.CanListProducts)
        {
            return Result<ProductSummaryResponse>.Failure("Your seller account is not approved for listing products yet.");
        }

        if (!await categories.AnyAsync(c => c.Id == request.CategoryId && !c.IsDeleted && c.IsActive, cancellationToken).ConfigureAwait(false))
        {
            return Result<ProductSummaryResponse>.Failure("The selected category is not available.");
        }

        var slugValue = string.IsNullOrWhiteSpace(request.Slug)
            ? await slugs.EnsureUniqueAsync(request.Name, async (candidate, ct) =>
                await products.AnyAsync(p => p.SlugValue == candidate, ct).ConfigureAwait(false), cancellationToken).ConfigureAwait(false)
            : request.Slug.Trim().ToLowerInvariant();

        if (await products.AnyAsync(p => p.SlugValue == slugValue, cancellationToken).ConfigureAwait(false))
        {
            return Result<ProductSummaryResponse>.Failure("A product with this slug already exists.");
        }

        var now = clock.UtcNow;
        var product = Product.Create(
            sellerId,
            request.CategoryId,
            request.Name,
            Slug.Create(slugValue),
            request.ShortDescription,
            request.Description,
            request.BasePrice,
            request.CompareAtPrice,
            request.Brand,
            request.Model,
            now);

        foreach (var image in request.Images)
        {
            product.AddImage(image.Url, image.AltText ?? request.Name, image.IsPrimary, now);
        }

        foreach (var variant in request.Variants)
        {
            var created = product.AddVariant(variant.Sku, variant.Name, variant.Price, 0, now);
            foreach (var option in variant.Options)
            {
                created.AddOption(option.Name, option.Value, now);
            }

            var stock = Math.Max(0, variant.InitialStock);
            var inventory = Inventory.Create(created.Id, product.Id, sellerId, stock,
                variant.LowStockThreshold ?? 5, now);
            await inventories.AddAsync(inventory, cancellationToken).ConfigureAwait(false);
        }

        foreach (var spec in request.Specifications)
        {
            product.AddSpecification(spec.Key, spec.Value, 0, now);
        }

        foreach (var tag in request.Tags ?? [])
        {
            product.AddTag(tag, now);
        }

        product.SubmitForApproval(now);

        await products.AddAsync(product, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.ProductCreated, nameof(Product), product.Id, product.Name,
            new { product.Name, product.BasePrice }, cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(product, cancellationToken).ConfigureAwait(false);

        return await GetByIdAsync(product.Id, cancellationToken).ConfigureAwait(false)
            is var result && result.IsSuccess
            ? Result<ProductSummaryResponse>.Success(new ProductSummaryResponse(
                product.Id, product.Name, product.SlugValue, product.ShortDescription, product.BasePrice,
                product.CompareAtPrice, product.DiscountPercentage,
                product.Images.FirstOrDefault(i => i.IsPrimary)?.Url,
                product.Images.FirstOrDefault(i => i.IsPrimary)?.AltText,
                product.SellerId, seller.BusinessName, string.Empty, string.Empty,
                product.CategoryId, string.Empty, string.Empty,
                product.RatingAverage, product.RatingCount, true, 0, product.IsFeatured, false, product.SoldCount, product.CreatedAt))
            : Result<ProductSummaryResponse>.Failure("The product was created but could not be reloaded.");
    }

    public async Task<Result<ProductSummaryResponse>> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken = default)
    {
        var product = await LoadOwnedProductAsync(id, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return Result<ProductSummaryResponse>.Failure("Product not found or not accessible.");
        }

        if (!string.IsNullOrWhiteSpace(request.Slug) && request.Slug.Trim().ToLowerInvariant() != product.SlugValue)
        {
            var slugValue = request.Slug.Trim().ToLowerInvariant();
            if (await products.AnyAsync(p => p.SlugValue == slugValue && p.Id != id, cancellationToken).ConfigureAwait(false))
            {
                return Result<ProductSummaryResponse>.Failure("A product with this slug already exists.");
            }

            product.ChangeSlug(Slug.Create(slugValue), clock.UtcNow);
        }

        product.UpdateDetails(request.Name, request.ShortDescription, request.Description, request.CategoryId, request.Brand, request.Model, clock.UtcNow);
        product.UpdatePricing(request.BasePrice, request.CompareAtPrice, clock.UtcNow);

        if (request.Tags is not null)
        {
            foreach (var tag in request.Tags)
            {
                product.AddTag(tag, clock.UtcNow);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.ProductUpdated, nameof(Product), product.Id, product.Name,
            new { product.Name, product.BasePrice }, cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(product, cancellationToken).ConfigureAwait(false);

        return Result<ProductSummaryResponse>.Success(new ProductSummaryResponse(
            product.Id, product.Name, product.SlugValue, product.ShortDescription, product.BasePrice,
            product.CompareAtPrice, product.DiscountPercentage,
            product.Images.FirstOrDefault(i => i.IsPrimary)?.Url,
            product.Images.FirstOrDefault(i => i.IsPrimary)?.AltText,
            product.SellerId, string.Empty, string.Empty, string.Empty,
            product.CategoryId, string.Empty, string.Empty,
            product.RatingAverage, product.RatingCount, true, 0, product.IsFeatured, false, product.SoldCount, product.CreatedAt));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await LoadOwnedProductAsync(id, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return Result.Failure("Product not found or not accessible.");
        }

        product.SoftDelete(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.ProductDeleted, nameof(Product), product.Id, product.Name, null, cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(product, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    public async Task<Result<ProductImageResponse>> AddImageAsync(Guid productId, AddProductImageRequest request, CancellationToken cancellationToken = default)
    {
        var product = await LoadOwnedProductAsync(productId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return Result<ProductImageResponse>.Failure("Product not found or not accessible.");
        }

        var image = product.AddImage(request.Url, request.AltText ?? product.Name, request.IsPrimary, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(product, cancellationToken).ConfigureAwait(false);

        return Result<ProductImageResponse>.Success(new ProductImageResponse(image.Id, image.Url, image.AltText, image.IsPrimary, image.SortOrder));
    }

    public async Task<Result> RemoveImageAsync(Guid productId, Guid imageId, CancellationToken cancellationToken = default)
    {
        var product = await LoadOwnedProductAsync(productId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return Result.Failure("Product not found or not accessible.");
        }

        product.RemoveImage(imageId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(product, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    public async Task<Result<ProductVariantResponse>> AddVariantAsync(Guid productId, AddProductVariantRequest request, CancellationToken cancellationToken = default)
    {
        var product = await LoadOwnedProductAsync(productId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return Result<ProductVariantResponse>.Failure("Product not found or not accessible.");
        }

        var now = clock.UtcNow;
        var variant = product.AddVariant(request.Sku, request.Name, request.Price, product.Variants.Count, now);
        foreach (var option in request.Options)
        {
            variant.AddOption(option.Name, option.Value, now);
        }

        var inventory = Inventory.Create(variant.Id, product.Id, product.SellerId, Math.Max(0, request.InitialStock),
            request.LowStockThreshold ?? 5, now);

        await inventories.AddAsync(inventory, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(product, cancellationToken).ConfigureAwait(false);

        return Result<ProductVariantResponse>.Success(new ProductVariantResponse(
            variant.Id, variant.Sku, variant.Name, variant.Price, variant.IsActive,
            inventory.AvailableQuantity, inventory.SellableQuantity > 0, inventory.LowStockThreshold,
            variant.Options.Select(o => new ProductVariantOptionResponse(o.Name, o.Value)).ToList()));
    }

    public async Task<Result> RemoveVariantAsync(Guid productId, Guid variantId, CancellationToken cancellationToken = default)
    {
        var product = await LoadOwnedProductAsync(productId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return Result.Failure("Product not found or not accessible.");
        }

        product.RemoveVariant(variantId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(product, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    public async Task<Result> SubmitForApprovalAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var product = await LoadOwnedProductAsync(productId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return Result.Failure("Product not found or not accessible.");
        }

        product.SubmitForApproval(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(product, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    public async Task<Result> ReviewApprovalAsync(Guid productId, ProductApprovalRequest request, CancellationToken cancellationToken = default)
    {
        var product = await products.GetByIdAsync(productId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return Result.Failure("Product not found.");
        }

        var now = clock.UtcNow;
        var previous = product.Status;

        if (request.Approve)
        {
            product.Approve(now);
        }
        else
        {
            product.Reject(request.Reason, request.Note ?? "Rejected by a marketplace moderator.", now);
        }

        product.AddDomainEvent(new ProductApprovalChangedEvent(product.Id, product.SellerId, product.Status, request.Note, now));

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(
            request.Approve ? AuditAction.ProductApproved : AuditAction.ProductRejected,
            nameof(Product), product.Id, product.Name,
            new { From = previous.ToString(), To = product.Status.ToString(), request.Note },
            cancellationToken).ConfigureAwait(false);

        await notificationService.NotifySellerAsync(
            await SellerUserIdAsync(product.SellerId, cancellationToken).ConfigureAwait(false),
            request.Approve ? NotificationType.ProductApproved : NotificationType.ProductRejected,
            request.Approve ? $"\"{product.Name}\" is live" : $"\"{product.Name}\" needs changes",
            request.Approve
                ? "Your product has been approved and is now visible in the marketplace."
                : request.Note ?? "Please review the feedback and resubmit.",
            $"/seller/products/{product.Id:N}",
            cancellationToken).ConfigureAwait(false);

        await InvalidateAsync(product, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> SetFeaturedAsync(Guid productId, bool isFeatured, CancellationToken cancellationToken = default)
    {
        var product = await products.GetByIdAsync(productId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return Result.Failure("Product not found.");
        }

        product.SetFeatured(isFeatured, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.ProductFeatured, nameof(Product), product.Id, product.Name,
            new { IsFeatured = isFeatured }, cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(product, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    private async Task<Product?> LoadOwnedProductAsync(Guid id, CancellationToken cancellationToken)
    {
        IQueryable<Product> query = products.Query()
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .Include(p => p.Variants.Select(v => v.Options));

        if (!currentUser.IsAdmin)
        {
            if (currentUser.SellerId is not { } sellerId)
            {
                return null;
            }

            query = query.Where(p => p.SellerId == sellerId);
        }

        return await query.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Guid> SellerUserIdAsync(Guid sellerId, CancellationToken cancellationToken)
    {
        var seller = await sellers.GetByIdAsync(sellerId, cancellationToken).ConfigureAwait(false);
        return seller?.UserId ?? Guid.Empty;
    }

    private static IQueryable<Product> ApplyFilters(IQueryable<Product> source, ProductQuery query)
    {
        if (currentUserIsAdmin())
        {
            // admins may list every state
        }
        else
        {
            source = source.Where(p => p.Status == ProductStatus.Published);
        }

        source = source.Where(p => !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            source = source.Where(p =>
                EF.Functions.Like(p.Name, term) ||
                EF.Functions.Like(p.ShortDescription, term) ||
                EF.Functions.Like(p.Description, term) ||
                p.Variants.Any(v => EF.Functions.Like(v.Sku, term)));
        }

        if (query.CategoryId is { } categoryId)
        {
            source = source.Where(p => p.CategoryId == categoryId);
        }

        if (query.SellerId is { } sellerId)
        {
            source = source.Where(p => p.SellerId == sellerId);
        }

        if (query.MinPrice is { } min)
        {
            source = source.Where(p => p.BasePrice >= min);
        }

        if (query.MaxPrice is { } max)
        {
            source = source.Where(p => p.BasePrice <= max);
        }

        if (query.MinRating is { } rating)
        {
            source = source.Where(p => p.RatingAverage >= rating);
        }

        if (query.OnSale == true)
        {
            source = source.Where(p => p.CompareAtPrice != null && p.CompareAtPrice > p.BasePrice);
        }

        if (query.InStock == true)
        {
            source = source.Where(p => p.Variants.Any(v => v.IsActive));
        }

        if (query.Status is { } status)
        {
            source = source.Where(p => p.Status == status);
        }

        return source;
    }

    // Injected indirectly to keep ApplyFilters static; set once per request by the caller.
    private static bool currentUserIsAdmin() => AdminScope.Value;

    private static readonly AsyncLocal<bool> AdminScope = new();

    internal IDisposable UseAdminScope(bool isAdmin)
    {
        AdminScope.Value = isAdmin;
        return new AdminScopeReset();
    }

    private sealed class AdminScopeReset : IDisposable
    {
        public void Dispose() => AdminScope.Value = false;
    }

    private async Task<List<ProductSummaryResponse>> HydrateAsync(List<ProductSummaryResponse> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return items;
        }

        var sellerIds = items.Select(i => i.SellerId).Distinct().ToList();
        var categoryIds = items.Select(i => i.CategoryId).Distinct().ToList();
        var productIds = items.Select(i => i.Id).ToList();

        var sellerNames = await sellers.Query().AsNoTracking()
            .Where(s => sellerIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.BusinessName, cancellationToken)
            .ConfigureAwait(false);

        var storeList = await stores.Query().AsNoTracking()
            .Where(s => sellerIds.Contains(s.SellerId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var categoryNames = await categories.Query().AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => (c.Name, c.SlugValue), cancellationToken)
            .ConfigureAwait(false);

        var stockRows = await inventories.Query().AsNoTracking()
            .Where(i => productIds.Contains(i.ProductId))
            .Select(i => new { i.ProductId, i.SellableQuantity })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var stock = stockRows
            .GroupBy(r => r.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.SellableQuantity));

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var store = storeList.FirstOrDefault(s => s.SellerId == item.SellerId);
            var category = categoryNames.GetValueOrDefault(item.CategoryId);
            var available = stock.GetValueOrDefault(item.Id);

            items[i] = item with
            {
                SellerName = sellerNames.GetValueOrDefault(item.SellerId, string.Empty),
                StoreName = store?.Name ?? string.Empty,
                StoreSlug = store?.SlugValue ?? string.Empty,
                CategoryName = category.Name ?? string.Empty,
                CategorySlug = category.SlugValue ?? string.Empty,
                AvailableQuantity = available,
                IsInStock = available > 0
            };
        }

        return items;
    }

    private static ProductSummaryResponse Project(
        Product p,
        string? imageUrl,
        string? imageAlt,
        string? sellerName,
        string? storeName,
        string? storeSlug,
        string? categoryName,
        int available,
        int ratingCount) =>
        new(p.Id, p.Name, p.SlugValue, p.ShortDescription, p.BasePrice, p.CompareAtPrice, p.DiscountPercentage,
            imageUrl, imageAlt, p.SellerId, sellerName ?? string.Empty, storeName ?? string.Empty, storeSlug ?? string.Empty,
            p.CategoryId, categoryName ?? string.Empty, categoryName ?? string.Empty, p.RatingAverage, ratingCount,
            available > 0, available, p.IsFeatured, false, p.SoldCount, p.CreatedAt);

    private async Task<ProductDetailResponse> BuildDetailAsync(Product product, CancellationToken cancellationToken)
    {
        var seller = await sellers.GetByIdAsync(product.SellerId, cancellationToken).ConfigureAwait(false);
        var store = await stores.Query().AsNoTracking()
            .FirstOrDefaultAsync(s => s.SellerId == product.SellerId, cancellationToken)
            .ConfigureAwait(false);
        var category = await categories.GetByIdAsync(product.CategoryId, cancellationToken).ConfigureAwait(false);
        var categoryName = category?.Name ?? string.Empty;
        var categorySlug = category?.SlugValue ?? string.Empty;


        var stockByVariant = await inventories.Query().AsNoTracking()
            .Where(i => i.ProductId == product.Id)
            .ToDictionaryAsync(i => i.ProductVariantId, cancellationToken)
            .ConfigureAwait(false);

        var totalSellable = stockByVariant.Values.Sum(i => i.SellableQuantity);

        var productReviews = await reviews.Query().AsNoTracking()
            .Where(r => r.ProductId == product.Id && r.IsVisible)
            .OrderByDescending(r => r.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var allRatings = await reviews.Query().AsNoTracking()
            .Where(r => r.ProductId == product.Id && r.IsVisible)
            .Select(r => r.Rating)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var related = await products.Query().AsNoTracking()
            .Where(p => p.CategoryId == product.CategoryId && p.Id != product.Id && p.Status == ProductStatus.Published && !p.IsDeleted)
            .OrderByDescending(p => p.SoldCount)
            .Take(8)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var relatedDtos = await HydrateAsync(related.Select(p => Project(p,
            p.Images.FirstOrDefault(i => i.IsPrimary)?.Url ?? p.Images.FirstOrDefault()?.Url,
            p.Images.FirstOrDefault(i => i.IsPrimary)?.AltText, null, null, null, null, 0, 0)).ToList(), cancellationToken)
            .ConfigureAwait(false);

        var variants = product.Variants
            .OrderBy(v => v.SortOrder)
            .Select(v =>
            {
                stockByVariant.TryGetValue(v.Id, out var inv);
                return new ProductVariantResponse(
                    v.Id,
                    v.Sku,
                    v.Name,
                    v.Price,
                    v.IsActive,
                    inv?.AvailableQuantity ?? 0,
                    (inv?.SellableQuantity ?? 0) > 0,
                    inv?.LowStockThreshold ?? 0,
                    v.Options.Select(o => new ProductVariantOptionResponse(o.Name, o.Value)).ToList());
            })
            .ToList();

        var isNew = clock.UtcNow - product.CreatedAt < TimeSpan.FromDays(30);

        return new ProductDetailResponse(
            product.Id,
            product.Name,
            product.SlugValue,
            product.ShortDescription,
            product.Description,
            product.BasePrice,
            product.CompareAtPrice,
            product.DiscountPercentage,
            product.SellerId,
            seller?.BusinessName ?? string.Empty,
            store?.Name ?? string.Empty,
            store?.SlugValue ?? string.Empty,
            store?.LogoUrl,
            store?.RatingAverage ?? 0m,
            store?.RatingCount ?? 0,
            product.CategoryId,
            categoryName,
            categorySlug,

            product.Brand,
            product.Model,
            product.Status,
            product.RatingAverage,
            product.RatingCount,
            product.ReviewCount,
            product.SoldCount,
            product.ViewCount,
            totalSellable > 0,
            totalSellable,
            product.IsFeatured,
            product.CreatedAt,
            product.Images.OrderBy(i => i.SortOrder).Select(i => new ProductImageResponse(i.Id, i.Url, i.AltText, i.IsPrimary, i.SortOrder)).ToList(),
            variants,
            product.Specifications.OrderBy(s => s.SortOrder).Select(s => new ProductSpecificationResponse(s.Key, s.Value, s.SortOrder)).ToList(),
            product.Tags.Select(t => t.Name).ToList(),
            relatedDtos.Select(r => new RelatedProductResponse(
                r.Id, r.Name, r.Slug, r.BasePrice, r.CompareAtPrice, r.DiscountPercentage, r.PrimaryImageUrl,
                r.RatingAverage, r.RatingCount, r.StoreName, r.IsInStock)).ToList(),
            productReviews.Select(r => new ReviewSummaryResponse(
                r.Id, r.Rating, r.Title, r.Body, r.CustomerId.ToString(), r.IsVerifiedPurchase, r.HelpfulCount, r.CreatedAt,
                r.Reply is null ? null : new ReviewReplyResponse(r.Reply.Id, r.Reply.Body, seller?.BusinessName ?? "Seller", r.Reply.CreatedAt))).ToList(),
            BuildBreakdown(allRatings));
    }

    internal static RatingBreakdownResponse BuildBreakdown(IReadOnlyCollection<int> ratings) =>
        new(
            ratings.Count == 0 ? 0m : decimal.Round((decimal)ratings.Average(), 2, MidpointRounding.AwayFromZero),
            ratings.Count,
            ratings.Count(r => r == 5),
            ratings.Count(r => r == 4),
            ratings.Count(r => r == 3),
            ratings.Count(r => r == 2),
            ratings.Count(r => r == 1));

    private async Task InvalidateAsync(Product product, CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(CacheKeys.Product(product.Id), cancellationToken).ConfigureAwait(false);
        await cache.RemoveAsync(CacheKeys.ProductBySlug(product.SlugValue), cancellationToken).ConfigureAwait(false);
        await cache.RemoveByTagAsync(CacheKeys.ProductTag(product.Id), cancellationToken).ConfigureAwait(false);
        await cache.RemoveByTagAsync(CacheKeys.SellerProductTag(product.SellerId), cancellationToken).ConfigureAwait(false);
        await cache.RemoveByTagAsync(CacheKeys.CatalogTag, cancellationToken).ConfigureAwait(false);
        await cache.RemoveByTagAsync(CacheKeys.CategoryTag, cancellationToken).ConfigureAwait(false);
        await cache.RemoveByTagAsync(CacheKeys.StoreProfileTag, cancellationToken).ConfigureAwait(false);
        await cache.RemoveAsync(CacheKeys.Home(), cancellationToken).ConfigureAwait(false);
    }
}
