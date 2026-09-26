using System.Linq.Expressions;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Domain.Common;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Repositories;

/// <summary>
/// Generic EF Core repository. Reads are <c>AsNoTracking</c> by default so list endpoints
/// never accidentally track thousands of rows; pass <c>trackChanges: true</c> when an
/// aggregate is being modified.
/// </summary>
public sealed class Repository<TEntity>(MarketplaceDbContext context) : IRepository<TEntity>
    where TEntity : Entity
{
    private readonly MarketplaceDbContext _context = context;

    public IQueryable<TEntity> Query() => _context.Set<TEntity>();

    /// <summary>Tracking query for write scenarios.</summary>
    public IQueryable<TEntity> TrackingQuery() => _context.Set<TEntity>();

    public Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Set<TEntity>().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await _context.Set<TEntity>().AddAsync(entity, cancellationToken).ConfigureAwait(false);
        return entity;
    }

    public void Update(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _context.Set<TEntity>().Update(entity);
    }

    public void Remove(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _context.Set<TEntity>().Remove(entity);
    }

    public Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) =>
        _context.Set<TEntity>().AsNoTracking().AnyAsync(predicate, cancellationToken);

    public Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) =>
        _context.Set<TEntity>().AsNoTracking().CountAsync(predicate, cancellationToken);

    public async Task<IReadOnlyList<TEntity>> ListAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) =>
        await _context.Set<TEntity>().AsNoTracking().Where(predicate).ToListAsync(cancellationToken).ConfigureAwait(false);
}

/// <summary>Wraps a <see cref="MarketplaceDbContext"/> in an explicit transaction scope.</summary>
public sealed class UnitOfWork(MarketplaceDbContext context) : IUnitOfWork
{
    private readonly MarketplaceDbContext _context = context;

    public async Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var existing = _context.Database.CurrentTransaction;
        if (existing is not null)
        {
            return new TransactionScopeAdapter(existing);
        }

        var transaction = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        return new TransactionScopeAdapter(transaction);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);

    public async Task ExecuteTransactionalAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var scope = await BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await operation(cancellationToken).ConfigureAwait(false);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await scope.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await scope.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed class TransactionScopeAdapter(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction) : ITransactionScope
    {
        public async Task CommitAsync(CancellationToken cancellationToken = default) =>
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // The transaction was already completed; nothing to roll back.
            }
        }

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
