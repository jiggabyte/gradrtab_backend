using GradrTab.DTOs;
using GradrTab.Models;
using GradrTab.Repositories;
using GradrTab.Tests.Support;

namespace GradrTab.Tests;

public class RepositoryTests
{
    [Fact]
    public async Task UserRepository_FindsByEmail()
    {
        var uow = new InMemoryUnitOfWork();
        await uow.Users.AddAsync(new User { FirstName = "Ada", LastName = "L", Email = "ada@example.com", PasswordHash = "x" });

        Assert.True(await uow.Users.ExistsWithEmailAsync("ada@example.com"));
        Assert.NotNull(await uow.Users.GetByEmailAsync("ada@example.com"));
        Assert.False(await uow.Users.ExistsWithEmailAsync("other@example.com"));
    }

    [Fact]
    public async Task DocumentRepository_ScopesByOwner()
    {
        var uow = new InMemoryUnitOfWork();
        var owner = Guid.NewGuid();
        TestHelpers.SeedDocument(uow, owner);
        TestHelpers.SeedDocument(uow, Guid.NewGuid(), "other.txt");

        Assert.Equal(1, uow.Documents.OwnedBy(owner).Count());
        var doc = uow.Documents.OwnedBy(owner).First();
        Assert.True(await uow.Documents.ExistsOwnedAsync(doc.Id, owner));
        Assert.False(await uow.Documents.ExistsOwnedAsync(doc.Id, Guid.NewGuid()));
        Assert.NotNull(await uow.Documents.GetOwnedTrackedAsync(doc.Id, owner));
    }

    [Fact]
    public async Task DocumentEntryRepository_DeletesForDocument()
    {
        var uow = new InMemoryUnitOfWork();
        var owner = Guid.NewGuid();
        var doc = TestHelpers.SeedDocument(uow, owner);

        Assert.Single(uow.DocumentEntries.ForDocument(doc.Id));
        await uow.DocumentEntries.DeleteForDocumentAsync(doc.Id);
        Assert.Empty(uow.DocumentEntries.ForDocument(doc.Id));
    }
}
