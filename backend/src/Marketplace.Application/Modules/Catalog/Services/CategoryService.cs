using Marketplace.Application.Common.Helpers;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Catalog.Abstractions;
using Marketplace.Application.Modules.Catalog.DTOs;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Modules.Catalog.Services;

/// <summary>
/// Category tree management. The public read path is cached and invalidated by tag on
/// every write so the storefront never serves a stale tree.
/// </summary>
public sealed class CategoryService(
    IRepository<Category> categories,
    IRepository<Domain.Catalog.Product> products,
    ICacheService cache,
    IUnitOfWork unitOfWork,
    IClock clock,
    IAuditService auditService,
    SlugGenerator slugs) : ICategoryService
{
    private static readonly TimeSpan TreeTtl = TimeSpan.FromMinutes(30);

    public async Task<IReadOnlyList<CategoryResponse>> GetTreeAsync(bool activeOnly, CancellationToken cancellationToken = default)
    {
        var cached = await cache.GetAsync<List<CategoryResponse>>(CacheKeys.Categories(), cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var all = await categories.Query()
            .AsNoTracking()
            .Where(c => !c.IsDeleted && (!activeOnly || c.IsActive))
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var tree = BuildTree(all);
        await cache.SetAsync(CacheKeys.Categories(), tree, TreeTtl, [CacheKeys.CategoryTag], cancellationToken).ConfigureAwait(false);
        return tree;
    }

    public async Task<Result<CategoryResponse>> GetByIdOrSlugAsync(string idOrSlug, CancellationToken cancellationToken = default)
    {
        var normalized = idOrSlug.Trim().ToLowerInvariant();

        var category = Guid.TryParse(idOrSlug, out var id)
            ? await categories.Query().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, cancellationToken).ConfigureAwait(false)
            : await categories.Query().AsNoTracking().FirstOrDefaultAsync(c => c.SlugValue == normalized && !c.IsDeleted, cancellationToken).ConfigureAwait(false);

        if (category is null)
        {
            return Result<CategoryResponse>.Failure("Category not found.", ResultErrorCodes.NotFound);
        }

        return Result<CategoryResponse>.Success(await BuildDetailAsync(category, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<CategoryResponse>> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ParentId is { } parentId)
        {
            var parentExists = await categories.AnyAsync(c => c.Id == parentId && !c.IsDeleted, cancellationToken).ConfigureAwait(false);
            if (!parentExists)
            {
                return Result<CategoryResponse>.Failure("The parent category does not exist.");
            }
        }

        var slugValue = string.IsNullOrWhiteSpace(request.Slug)
            ? await slugs.EnsureUniqueAsync(request.Name, async (candidate, ct) =>
                await categories.AnyAsync(c => c.SlugValue == candidate, ct).ConfigureAwait(false), cancellationToken).ConfigureAwait(false)
            : request.Slug.Trim().ToLowerInvariant();

        if (await categories.AnyAsync(c => c.SlugValue == slugValue, cancellationToken).ConfigureAwait(false))
        {
            return Result<CategoryResponse>.Failure("A category with this slug already exists.");
        }

        var now = clock.UtcNow;
        var category = Category.Create(
            request.Name,
            Slug.Create(slugValue),
            request.Description,
            request.ParentId,
            request.DisplayOrder,
            now);

        await categories.AddAsync(category, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.CategoryCreated, nameof(Category), category.Id, category.Name,
            new { category.Name, ParentId = category.ParentId }, cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(cancellationToken).ConfigureAwait(false);

        return Result<CategoryResponse>.Success(await BuildDetailAsync(category, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<CategoryResponse>> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var category = await categories.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (category is null)
        {
            return Result<CategoryResponse>.Failure("Category not found.", ResultErrorCodes.NotFound);
        }

        category.Update(request.Name, request.Description, request.ImageUrl, request.DisplayOrder, clock.UtcNow);

        if (!string.IsNullOrWhiteSpace(request.Slug) && request.Slug.Trim().ToLowerInvariant() != category.SlugValue)
        {
            var slugValue = request.Slug.Trim().ToLowerInvariant();
            if (await categories.AnyAsync(c => c.SlugValue == slugValue && c.Id != id, cancellationToken).ConfigureAwait(false))
            {
                return Result<CategoryResponse>.Failure("A category with this slug already exists.");
            }

            category.ChangeSlug(Slug.Create(slugValue), clock.UtcNow);
        }

        category.SetActive(request.IsActive, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.CategoryUpdated, nameof(Category), category.Id, category.Name,
            new { category.Name, category.IsActive }, cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(cancellationToken).ConfigureAwait(false);

        return Result<CategoryResponse>.Success(await BuildDetailAsync(category, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await categories.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (category is null)
        {
            return Result.Failure("Category not found.", ResultErrorCodes.NotFound);
        }

        var childCount = await categories.CountAsync(c => c.ParentId == id && !c.IsDeleted, cancellationToken).ConfigureAwait(false);
        if (childCount > 0)
        {
            return Result.Failure("Move or remove the child categories first.");
        }

        var productCount = await products.CountAsync(p => p.CategoryId == id && !p.IsDeleted, cancellationToken).ConfigureAwait(false);
        if (productCount > 0)
        {
            return Result.Failure("This category still contains products. Deactivate it instead of deleting it.");
        }

        category.SoftDelete(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditService.RecordAsync(AuditAction.CategoryDeleted, nameof(Category), category.Id, category.Name, null, cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    public async Task<Result> ReorderAsync(IReadOnlyList<CategoryOrderRequest> items, CancellationToken cancellationToken = default)
    {
        var ids = items.Select(i => i.Id).ToList();
        var existing = await categories.Query().Where(c => ids.Contains(c.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var item in items)
        {
            existing.FirstOrDefault(c => c.Id == item.Id)?.Reorder(item.DisplayOrder, clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    internal static IReadOnlyList<CategoryResponse> BuildTree(IReadOnlyCollection<Category> all)
    {
        var byParent = all
            .GroupBy(c => c.ParentId ?? Guid.Empty)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ToList());

        return BuildLevel(Guid.Empty, byParent, []);
    }

    private static List<CategoryResponse> BuildLevel(
        Guid parentId,
        Dictionary<Guid, List<Category>> byParent,
        HashSet<Guid> visited)
    {
        var result = new List<CategoryResponse>();
        if (!byParent.TryGetValue(parentId, out var children))
        {
            return result;
        }

        foreach (var child in children)
        {
            if (!visited.Add(child.Id))
            {
                continue;
            }

            result.Add(new CategoryResponse(
                child.Id,
                child.ParentId,
                child.Name,
                child.SlugValue,
                child.Description,
                child.ImageUrl,
                child.DisplayOrder,
                child.IsActive,
                child.ProductCount,
                BuildLevel(child.Id, byParent, visited),
                []));
        }

        return result;
    }

    private async Task<CategoryResponse> BuildDetailAsync(Category category, CancellationToken cancellationToken)
    {
        var all = await categories.Query()
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byId = all.ToDictionary(c => c.Id);
        var breadcrumb = new List<BreadcrumbItemResponse>();
        var cursor = category;

        while (cursor is not null)
        {
            breadcrumb.Insert(0, new BreadcrumbItemResponse(cursor.Name, cursor.SlugValue, $"/categories/{cursor.SlugValue}"));
            cursor = cursor.ParentId is { } pid && byId.TryGetValue(pid, out var parent) ? parent : null;
        }

        var children = all
            .Where(c => c.ParentId == category.Id)
            .Select(c => new CategoryResponse(
                c.Id, c.ParentId, c.Name, c.SlugValue, c.Description, c.ImageUrl,
                c.DisplayOrder, c.IsActive, c.ProductCount, [], []))
            .ToList();

        return new CategoryResponse(
            category.Id,
            category.ParentId,
            category.Name,
            category.SlugValue,
            category.Description,
            category.ImageUrl,
            category.DisplayOrder,
            category.IsActive,
            category.ProductCount,
            children,
            breadcrumb);
    }

    private async Task InvalidateAsync(CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(CacheKeys.Categories(), cancellationToken).ConfigureAwait(false);
        await cache.RemoveByTagAsync(CacheKeys.CategoryTag, cancellationToken).ConfigureAwait(false);
        await cache.RemoveByTagAsync(CacheKeys.CatalogTag, cancellationToken).ConfigureAwait(false);
        await cache.RemoveAsync(CacheKeys.Home(), cancellationToken).ConfigureAwait(false);
    }
}
