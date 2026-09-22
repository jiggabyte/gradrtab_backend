using GradrTab.Models;

namespace GradrTab.Repositories;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> ExistsWithEmailAsync(string email, CancellationToken cancellationToken = default);
}

public interface IDocumentRepository : IRepository<Document>
{
    // Only documents owned by the given user are visible.
    IQueryable<Document> OwnedBy(Guid userId, bool asNoTracking = true);

    Task<Document?> GetOwnedAsync(Guid id, Guid userId, bool asNoTracking = true, CancellationToken cancellationToken = default);

    Task<bool> ExistsOwnedAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    // Tracked entity used for deletes where cascade delete must run.
    Task<Document?> GetOwnedTrackedAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
}

public interface IDocumentEntryRepository : IRepository<DocumentEntry>
{
    IQueryable<DocumentEntry> ForDocument(Guid documentId, bool asNoTracking = true);

    Task<int> DeleteForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);
}
