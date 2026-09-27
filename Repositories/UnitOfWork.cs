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
        PasswordResetTokens = new PasswordResetTokenRepository(context);
        Rubrics = new RubricRepository(context);
    }

    public IUserRepository Users { get; }

    public IDocumentRepository Documents { get; }

    public IDocumentEntryRepository DocumentEntries { get; }

    public IPasswordResetTokenRepository PasswordResetTokens { get; }

    public IRubricRepository Rubrics { get; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);

    public void Dispose() => _context.Dispose();
}
