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

public interface IPasswordResetTokenRepository : IRepository<PasswordResetToken>
{
    // Only a token that is neither used nor expired, tracked so it can be burnt
    Task<PasswordResetToken?> GetActiveByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    // Marks every outstanding token of a user as used
    Task<int> InvalidateForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    // Used to throttle how many links one account may ask for
    Task<int> CountSinceAsync(Guid userId, DateTime since, CancellationToken cancellationToken = default);
}

public interface IRubricRepository : IRepository<Rubric>
{
    // The criteria and levels come with the rubric, otherwise the response would
    // come back empty
    Task<Rubric?> GetWithChildrenAsync(Guid id, CancellationToken cancellationToken = default);

    // The client supplied key, optionally narrowed down to one owner
    Task<Rubric?> GetByKeyAsync(string rubricKey, Guid? userId = null, CancellationToken cancellationToken = default);

    Task<Rubric?> GetWithChildrenByKeyAsync(string rubricKey, Guid? userId = null, CancellationToken cancellationToken = default);

    // Tracked entity, needed where a delete has to run
    Task<Rubric?> GetTrackedAsync(Guid id, CancellationToken cancellationToken = default);
}

