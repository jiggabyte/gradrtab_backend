using System.Text.Json;
using GradrTab.Configuration;
using GradrTab.DTOs;
using GradrTab.Models;
using GradrTab.Repositories;
using GradrTab.Services.Extraction;
using Microsoft.Extensions.Options;

namespace GradrTab.Services;

// Stores an uploaded multipart file and reads the information out of it.
public interface IDocumentProcessingService
{
    // Validates, stores and processes one uploaded file. Validation problems are
    // reported on the returned result instead of failing the whole request.
    Task<DocumentUploadResultDto> IngestAsync(IFormFile file, Guid? userId, CancellationToken cancellationToken = default);

    // Runs the extraction pipeline again over the stored copy of a document
    Task<DocumentUploadResultDto?> ReprocessAsync(Guid documentId, CancellationToken cancellationToken = default);
}

public sealed class DocumentProcessingService : IDocumentProcessingService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileStorageService _storage;
    private readonly IDocumentExtractorResolver _resolver;
    private readonly DocumentProcessingOptions _options;
    private readonly ILogger<DocumentProcessingService> _logger;

    private static readonly JsonSerializerOptions SerializerOptions = new();

    public DocumentProcessingService(
        IUnitOfWork unitOfWork,
        IFileStorageService storage,
        IDocumentExtractorResolver resolver,
        IOptions<DocumentProcessingOptions> options,
        ILogger<DocumentProcessingService> logger)
    {
        _unitOfWork = unitOfWork;
        _storage = storage;
        _resolver = resolver;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DocumentUploadResultDto> IngestAsync(
        IFormFile file,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var fileName = Path.GetFileName(file.FileName ?? string.Empty);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Reject(fileName, 0, "The uploaded file does not have a name.");
        }

        if (file.Length <= 0)
        {
            return Reject(fileName, 0, "The uploaded file is empty.");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (extension.Length == 0)
        {
            return Reject(fileName, file.Length, "The uploaded file does not have an extension, so its type cannot be determined.");
        }

        var extractor = _resolver.Resolve(fileName);

        if (extractor is null || !_options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return Reject(
                fileName,
                file.Length,
                $"Files of type '{extension}' are not supported. Supported types: {_resolver.DescribeSupportedExtensions()}.");
        }

        if (file.Length > _options.MaxFileSizeBytes)
        {
            return Reject(
                fileName,
                file.Length,
                $"The file is {FormatMegabytes(file.Length)} which is larger than the allowed maximum of {FormatMegabytes(_options.MaxFileSizeBytes)}.");
        }

        var document = new Document
        {
            OriginalFileName = fileName,
            ContentType = file.ContentType ?? string.Empty,
            FileExtension = extension,
            FileSizeBytes = file.Length,
            DocumentType = extractor.DocumentType,
            Status = DocumentStatus.Pending,
            UploadedByUserId = userId
        };

        // The generated name keeps the original extension so the file stays recognisable
        var storedFileName = $"{document.Id:N}{extension}";

        try
        {
            await using var uploadStream = file.OpenReadStream();
            document.StoragePath = await _storage.SaveAsync(uploadStream, storedFileName, cancellationToken);
            document.StoredFileName = Path.GetFileName(document.StoragePath);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Storing the upload {FileName} failed", fileName);
            return Reject(fileName, file.Length, $"The file could not be stored: {exception.Message}");
        }

        await _unitOfWork.Documents.AddAsync(document, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await RunExtractionAsync(document, extractor, cancellationToken);

        return ToUploadResult(document);
    }

    public async Task<DocumentUploadResultDto?> ReprocessAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _unitOfWork.Documents.GetByIdAsync(documentId, cancellationToken);

        if (document is null)
        {
            return null;
        }

        var extractor = _resolver.Resolve(document.OriginalFileName);

        if (extractor is null)
        {
            document.Status = DocumentStatus.Failed;
            document.ErrorMessage = $"Files of type '{document.FileExtension}' are not supported.";
            document.ProcessedAt = DateTime.UtcNow;
            document.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToUploadResult(document);
        }

        // Start from a clean slate so a partial previous run cannot leave rows behind
        await _unitOfWork.DocumentEntries.DeleteForDocumentAsync(document.Id, cancellationToken);

        await RunExtractionAsync(document, extractor, cancellationToken);

        return ToUploadResult(document);
    }

    private static string FormatMegabytes(long bytes) => $"{bytes / 1024d / 1024d:0.##} MB";

    private static DocumentUploadResultDto Reject(string fileName, long fileSize, string reason) =>
        new(null, fileName, fileSize, "Rejected", null, reason, []);

    // Runs the extractor and writes everything it found onto the document.
    private async Task RunExtractionAsync(
        Document document,
        IDocumentExtractor extractor,
        CancellationToken cancellationToken)
    {
        document.Status = DocumentStatus.Processing;
        document.ErrorMessage = null;
        document.ProcessedAt = null;
        document.UpdatedAt = DateTime.UtcNow;

        try
        {
            await using var stream = await _storage.OpenReadAsync(document.StoragePath, cancellationToken);

            var extraction = await extractor.ExtractAsync(stream, document.OriginalFileName, cancellationToken);

            await ApplyExtractionAsync(document, extraction, cancellationToken);
            document.Status = DocumentStatus.Completed;

            _logger.LogInformation(
                "Processed {FileName} as {DocumentType} with {EntryCount} entries and {CharacterCount} characters",
                document.OriginalFileName,
                document.DocumentType,
                document.EntryCount,
                document.CharacterCount);
        }
        catch (OperationCanceledException)
        {
            // The client went away, leave the document as it is
            throw;
        }
        catch (Exception exception)
        {
            document.Status = DocumentStatus.Failed;
            document.ErrorMessage = exception.Message;
            document.EntryCount = 0;

            _logger.LogError(exception, "Processing {FileName} failed", document.OriginalFileName);
        }

        document.ProcessedAt = DateTime.UtcNow;
        document.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // Copies the extraction result onto the document and queues the entry rows.
    private async Task ApplyExtractionAsync(Document document, DocumentExtractionResult extraction, CancellationToken cancellationToken)
    {
        var text = extraction.Text ?? string.Empty;

        if (text.Length > _options.MaxStoredTextCharacters)
        {
            text = text[.._options.MaxStoredTextCharacters];
            extraction.Warnings.Add($"Only the first {_options.MaxStoredTextCharacters} characters of the extracted text were stored.");
        }

        document.ExtractedText = text.Length > 0 ? text : null;
        document.CharacterCount = text.Length;
        document.WordCount = TextUtilities.CountWords(text);
        document.PageCount = extraction.PageCount;
        document.ErrorMessage = null;

        document.Metadata = extraction.Metadata.Count > 0
            ? JsonSerializer.Serialize(extraction.Metadata, SerializerOptions)
            : null;

        document.Warnings = extraction.Warnings.Count > 0
            ? JsonSerializer.Serialize(extraction.Warnings, SerializerOptions)
            : null;

        var storedEntries = 0;

        foreach (var entry in extraction.Entries.Take(_options.MaxEntriesPerDocument))
        {
            await _unitOfWork.DocumentEntries.AddAsync(new DocumentEntry
            {
                DocumentId = document.Id,
                EntryIndex = entry.Index,
                EntryType = Truncate(entry.EntryType, 40),
                Key = Truncate(entry.Key, 255),
                Content = entry.Content,
                Data = entry.Data is { Count: > 0 }
                    ? JsonSerializer.Serialize(entry.Data, SerializerOptions)
                    : null
            }, cancellationToken);

            storedEntries++;
        }

        document.EntryCount = storedEntries;
    }

    private static string Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private static DocumentUploadResultDto ToUploadResult(Document document) => new(
        document.Id,
        document.OriginalFileName,
        document.FileSizeBytes,
        document.Status.ToString(),
        document.DocumentType.ToString(),
        document.ErrorMessage,
        DeserializeWarnings(document.Warnings));

    private static IReadOnlyList<string> DeserializeWarnings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, SerializerOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}