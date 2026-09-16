using GradrTab.Data;
using GradrTab.Models;
using Microsoft.EntityFrameworkCore;

namespace GradrTab.Repositories;

public sealed class UserRepository(AppDbContext context) : Repository<User>(context), IUserRepository
{
    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        Set.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

    public Task<bool> ExistsWithEmailAsync(string email, CancellationToken cancellationToken = default) =>
        Set.AnyAsync(u => u.Email == email, cancellationToken);
}

public sealed class DocumentRepository(AppDbContext context) : Repository<Document>(context), IDocumentRepository
{
    public IQueryable<Document> OwnedBy(Guid userId, bool asNoTracking = true)
    {
        var query = Set.Where(d => d.UploadedByUserId == userId);
        return asNoTracking ? query.AsNoTracking() : query;
    }

    public Task<Document?> GetOwnedAsync(Guid id, Guid userId, bool asNoTracking = true, CancellationToken cancellationToken = default)
    {
        var query = OwnedBy(userId, asNoTracking);
        return query.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public Task<bool> ExistsOwnedAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
        OwnedBy(userId, asNoTracking: true).AnyAsync(d => d.Id == id, cancellationToken);

    public Task<Document?> GetOwnedTrackedAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
        Set.FirstOrDefaultAsync(d => d.Id == id && d.UploadedByUserId == userId, cancellationToken);
}

public sealed class DocumentEntryRepository(AppDbContext context) : Repository<DocumentEntry>(context), IDocumentEntryRepository
{
    public IQueryable<DocumentEntry> ForDocument(Guid documentId, bool asNoTracking = true)
    {
        var query = Set.Where(e => e.DocumentId == documentId);
        return asNoTracking ? query.AsNoTracking() : query;
    }

    public Task<int> DeleteForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        Set.Where(e => e.DocumentId == documentId).ExecuteDeleteAsync(cancellationToken);
}
