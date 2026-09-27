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

public sealed class PasswordResetTokenRepository(AppDbContext context)
    : Repository<PasswordResetToken>(context), IPasswordResetTokenRepository
{
    public Task<PasswordResetToken?> GetActiveByHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        Set.FirstOrDefaultAsync(
            t => t.TokenHash == tokenHash && t.UsedAt == null && t.ExpiresAt > DateTime.UtcNow,
            cancellationToken);

    // Stamps the outstanding tokens as used, the caller commits them through
    // IUnitOfWork.SaveChangesAsync so there is still a single save boundary.
    public async Task<int> InvalidateForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var outstanding = await Set
            .Where(t => t.UserId == userId && t.UsedAt == null)
            .ToListAsync(cancellationToken);

        if (outstanding.Count == 0)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        foreach (var token in outstanding)
        {
            token.UsedAt = now;
        }

        return outstanding.Count;
    }

    public Task<int> CountSinceAsync(Guid userId, DateTime since, CancellationToken cancellationToken = default) =>
        Set.CountAsync(t => t.UserId == userId && t.CreatedAt >= since, cancellationToken);
}

public sealed class RubricRepository(AppDbContext context) : Repository<Rubric>(context), IRubricRepository
{
    // OrderBy is applied so the criteria come back in the submitted order.
    // Include already returns a fresh queryable, so it is never tracked twice.
    private IQueryable<Rubric> WithChildren() =>
        Set
            .Include(r => r.Criteria.OrderBy(c => c.Order))
            .ThenInclude(c => c.Levels.OrderBy(l => l.Order))
            .AsNoTracking();

    public Task<Rubric?> GetWithChildrenAsync(Guid id, CancellationToken cancellationToken = default) =>
        WithChildren().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<Rubric?> GetByKeyAsync(string rubricKey, Guid? userId = null, CancellationToken cancellationToken = default) =>
        Set.FirstOrDefaultAsync(
            r => r.RubricKey == rubricKey && (userId == null || r.UploadedByUserId == userId),
            cancellationToken);

    public Task<Rubric?> GetWithChildrenByKeyAsync(
        string rubricKey,
        Guid? userId = null,
        CancellationToken cancellationToken = default) =>
        WithChildren().FirstOrDefaultAsync(
            r => r.RubricKey == rubricKey && (userId == null || r.UploadedByUserId == userId),
            cancellationToken);

    public Task<Rubric?> GetTrackedAsync(Guid id, CancellationToken cancellationToken = default) =>
        Set.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
}

