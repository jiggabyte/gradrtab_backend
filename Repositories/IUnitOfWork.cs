namespace GradrTab.Repositories;

// Unit of work exposing repositories plus a single SaveChanges
// boundary. Controllers/services must use this instead of AppDbContext.
public interface IUnitOfWork : IDisposable
{
    IUserRepository Users { get; }

    IDocumentRepository Documents { get; }

    IDocumentEntryRepository DocumentEntries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
