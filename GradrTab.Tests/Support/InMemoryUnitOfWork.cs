using GradrTab.Models;
using GradrTab.Repositories;

namespace GradrTab.Tests.Support;

// In-memory UnitOfWork used by tests so controller/service tests do not
// touch EF Core or SQLite at all.
public sealed class InMemoryUnitOfWork : IUnitOfWork
{
    private readonly List<User> _users = [];
    private readonly List<Document> _documents = [];
    private readonly List<DocumentEntry> _entries = [];

    public InMemoryUnitOfWork()
    {
        Users = new ListUserRepository(_users);
        Documents = new ListDocumentRepository(_documents);
        DocumentEntries = new ListDocumentEntryRepository(_entries);
    }

    public IUserRepository Users { get; }

    public IDocumentRepository Documents { get; }

    public IDocumentEntryRepository DocumentEntries { get; }

    public int SaveCalls { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCalls++;
        return Task.FromResult(0);
    }

    public void Dispose()
    {
    }

    private sealed class ListUserRepository(List<User> store) : IUserRepository
    {
        public IQueryable<User> Query(bool asNoTracking = false) => store.AsQueryable();

        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(store.FirstOrDefault(u => u.Id == id));

        public Task AddAsync(User entity, CancellationToken cancellationToken = default)
        {
            store.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(User entity)
        {
        }

        public void Remove(User entity) => store.Remove(entity);

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(store.FirstOrDefault(u => u.Email == email));

        public Task<bool> ExistsWithEmailAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(store.Any(u => u.Email == email));
    }

    private sealed class ListDocumentRepository(List<Document> store) : IDocumentRepository
    {
        public IQueryable<Document> Query(bool asNoTracking = false) => store.AsQueryable();

        public Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(store.FirstOrDefault(d => d.Id == id));

        public Task AddAsync(Document entity, CancellationToken cancellationToken = default)
        {
            store.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(Document entity)
        {
        }

        public void Remove(Document entity) => store.Remove(entity);

        public IQueryable<Document> OwnedBy(Guid userId, bool asNoTracking = true) =>
            store.Where(d => d.UploadedByUserId == userId).AsQueryable();

        public Task<Document?> GetOwnedAsync(Guid id, Guid userId, bool asNoTracking = true, CancellationToken cancellationToken = default) =>
            Task.FromResult(store.FirstOrDefault(d => d.Id == id && d.UploadedByUserId == userId));

        public Task<bool> ExistsOwnedAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(store.Any(d => d.Id == id && d.UploadedByUserId == userId));

        public Task<Document?> GetOwnedTrackedAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(store.FirstOrDefault(d => d.Id == id && d.UploadedByUserId == userId));
    }

    private sealed class ListDocumentEntryRepository(List<DocumentEntry> store) : IDocumentEntryRepository
    {
        public IQueryable<DocumentEntry> Query(bool asNoTracking = false) => store.AsQueryable();

        public Task<DocumentEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(store.FirstOrDefault(e => e.Id == id));

        public Task AddAsync(DocumentEntry entity, CancellationToken cancellationToken = default)
        {
            store.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(DocumentEntry entity)
        {
        }

        public void Remove(DocumentEntry entity) => store.Remove(entity);

        public IQueryable<DocumentEntry> ForDocument(Guid documentId, bool asNoTracking = true) =>
            store.Where(e => e.DocumentId == documentId).AsQueryable();

        public Task<int> DeleteForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            var removed = store.RemoveAll(e => e.DocumentId == documentId);
            return Task.FromResult(removed);
        }
    }
}
