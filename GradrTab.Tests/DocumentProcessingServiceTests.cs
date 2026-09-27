using GradrTab.Models;
using GradrTab.Tests.Support;

namespace GradrTab.Tests;

public class DocumentProcessingServiceTests
{
    [Fact]
    public async Task Ingest_RejectsEmptyFile()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateProcessingService(
            uow, FakeFileStorageService.Create(), new FakeExtractorResolver(TestHelpers.TextExtractor()));
        var file = TestHelpers.FormFileFromText("empty.txt", string.Empty);

        var result = await service.IngestAsync(file, Guid.NewGuid());

        Assert.Null(result.Id);
        Assert.Equal("Rejected", result.Status);
    }

    [Fact]
    public async Task Ingest_RejectsUnsupportedExtension()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateProcessingService(
            uow, FakeFileStorageService.Create(), new FakeExtractorResolver(TestHelpers.TextExtractor()));
        var file = TestHelpers.FormFileFromText("archive.zip", "data");

        var result = await service.IngestAsync(file, Guid.NewGuid());

        Assert.Null(result.Id);
        Assert.Contains("not supported", result.ErrorMessage);
    }

    [Fact]
    public async Task Ingest_StoresTextDocumentAndEntries()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateProcessingService(
            uow, FakeFileStorageService.Create(), new FakeExtractorResolver(TestHelpers.TextExtractor()));
        var userId = Guid.NewGuid();
        var file = TestHelpers.FormFileFromText("notes.txt", "hello world\nsecond line");

        var result = await service.IngestAsync(file, userId);

        Assert.NotNull(result.Id);
        Assert.Equal("Completed", result.Status);
        var stored = await uow.Documents.GetByIdAsync(result.Id.Value);
        Assert.NotNull(stored);
        Assert.Equal(DocumentType.Text, stored!.DocumentType);
        Assert.Equal(2, stored.EntryCount);
        Assert.Equal(2, uow.DocumentEntries.ForDocument(stored.Id).Count());
    }

    [Fact]
    public async Task Reprocess_ReturnsNullForMissingDocument()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateProcessingService(
            uow, FakeFileStorageService.Create(), new FakeExtractorResolver(TestHelpers.TextExtractor()));

        Assert.Null(await service.ReprocessAsync(Guid.NewGuid()));
    }
}
