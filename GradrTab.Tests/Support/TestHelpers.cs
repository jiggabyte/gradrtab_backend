using GradrTab.Configuration;
using GradrTab.DTOs;
using GradrTab.Models;
using GradrTab.Repositories;
using GradrTab.Services;
using GradrTab.Services.Extraction;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GradrTab.Tests.Support;

public static class TestHelpers
{
    public static DocumentProcessingOptions DefaultOptions() => new()
    {
        MaxFileSizeBytes = 10 * 1024 * 1024,
        MaxFilesPerRequest = 20,
        MaxEntriesPerDocument = 2000,
        MaxStoredTextCharacters = 1_000_000,
        StorageRoot = Path.Combine(Path.GetTempPath(), "gradrtab-tests"),
        AllowedExtensions = [".pdf", ".docx", ".doc", ".png", ".jpg", ".csv", ".json", ".txt", ".md"],
    };

    public static IOptions<DocumentProcessingOptions> OptionsWrapper(DocumentProcessingOptions? options = null) =>
        Options.Create(options ?? DefaultOptions());

    public static IDocumentProcessingService CreateProcessingService(
        IUnitOfWork unitOfWork,
        IFileStorageService storage,
        IDocumentExtractorResolver resolver,
        DocumentProcessingOptions? options = null) =>
        new DocumentProcessingService(
            unitOfWork,
            storage,
            resolver,
            OptionsWrapper(options),
            NullLogger<DocumentProcessingService>.Instance);

    public static IDocumentExtractor TextExtractor() => new TextDocumentExtractor();

    public static FormFile FormFileFromText(string fileName, string content, string contentType = "text/plain")
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "files", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    public static Document SeedDocument(
        InMemoryUnitOfWork uow,
        Guid userId,
        string fileName = "notes.txt",
        DocumentStatus status = DocumentStatus.Completed,
        DocumentType type = DocumentType.Text,
        string? text = "hello world")
    {
        var document = new Document
        {
            OriginalFileName = fileName,
            StoredFileName = Guid.NewGuid() + ".txt",
            StoragePath = Guid.NewGuid() + ".txt",
            ContentType = "text/plain",
            FileExtension = ".txt",
            FileSizeBytes = 11,
            DocumentType = type,
            Status = status,
            ExtractedText = text,
            CharacterCount = text?.Length ?? 0,
            WordCount = 2,
            EntryCount = 1,
            UploadedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ProcessedAt = DateTime.UtcNow,
        };
        uow.Documents.AddAsync(document).GetAwaiter().GetResult();
        uow.DocumentEntries.AddAsync(new DocumentEntry
        {
            DocumentId = document.Id,
            EntryIndex = 0,
            EntryType = "Line",
            Key = "Line 1",
            Content = text,
        }).GetAwaiter().GetResult();
        uow.SaveChangesAsync().GetAwaiter().GetResult();
        return document;
    }
}
