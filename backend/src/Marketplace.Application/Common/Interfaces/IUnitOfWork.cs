namespace Marketplace.Application.Common.Interfaces;

/// <summary>
/// Transaction boundary for operations that must be atomic (checkout, refunds,
/// commission creation). Implemented over EF Core in Infrastructure.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task ExecuteTransactionalAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
}

/// <summary>A started transaction, disposed asynchronously when the scope ends.</summary>
public interface ITransactionScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>Marker so generic repositories can expose the underlying queryable.</summary>
public interface IRepository<TEntity>
    where TEntity : class
{
    IQueryable<TEntity> Query();

    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    void Update(TEntity entity);

    void Remove(TEntity entity);

    Task<bool> AnyAsync(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    Task<int> CountAsync(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TEntity>> ListAsync(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
}
