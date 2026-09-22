using GradrTab.Data;

namespace GradrTab.Repositories;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
        Users = new UserRepository(context);
        Documents = new DocumentRepository(context);
        DocumentEntries = new DocumentEntryRepository(context);
    }

    public IUserRepository Users { get; }

    public IDocumentRepository Documents { get; }

    public IDocumentEntryRepository DocumentEntries { get; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);

    public void Dispose() => _context.Dispose();
}
