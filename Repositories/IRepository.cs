namespace GradrTab.Repositories;

// Generic CRUD contract shared by all entity repositories.
// Query() exposes IQueryable for flexible filtering/paging while
// keeping EF Core details behind the repository boundary.
public interface IRepository<T> where T : class
{
    IQueryable<T> Query(bool asNoTracking = false);

    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(T entity, CancellationToken cancellationToken = default);

    void Update(T entity);

    void Remove(T entity);
}
