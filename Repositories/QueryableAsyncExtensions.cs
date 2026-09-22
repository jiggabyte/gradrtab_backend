using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace GradrTab.Repositories;

// Lets controllers/services keep using async LINQ whether the underlying
// IQueryable comes from EF Core (production) or plain LINQ-to-Objects
// (the InMemoryUnitOfWork used in tests).
public static class QueryableAsyncExtensions
{
    public static Task<int> CountAsyncCompat<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) =>
        source.Provider is IAsyncQueryProvider
            ? EntityFrameworkQueryableExtensions.CountAsync(source, cancellationToken)
            : Task.FromResult(source.Count());

    public static Task<List<T>> ToListAsyncCompat<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) =>
        source.Provider is IAsyncQueryProvider
            ? EntityFrameworkQueryableExtensions.ToListAsync(source, cancellationToken)
            : Task.FromResult(source.ToList());
}
