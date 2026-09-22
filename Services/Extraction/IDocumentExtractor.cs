using GradrTab.Models;

namespace GradrTab.Services.Extraction;

// A single piece of information read out of an uploaded document.
public sealed record ExtractedEntry(
    int Index,
    string EntryType,
    string? Key,
    string? Content,
    Dictionary<string, object?>? Data);

// Everything an extractor managed to read out of one uploaded file.
public sealed class DocumentExtractionResult
{
    // The plain text representation of the whole document
    public string? Text { get; set; }

    // Pages for PDF/Word, null when the format does not report it
    public int? PageCount { get; set; }

    // Structured rows/records/fields found inside the document
    public List<ExtractedEntry> Entries { get; } = [];

    // Type specific details that are serialized to the jsonb Metadata column
    public Dictionary<string, object?> Metadata { get; } = [];

    // Non fatal notes, e.g. truncated rows or an unavailable OCR engine
    public List<string> Warnings { get; } = [];
}

// Reads the information out of one document format.
public interface IDocumentExtractor
{
    DocumentType DocumentType { get; }

    // Extensions handled by this extractor, including the leading dot
    IReadOnlyCollection<string> SupportedExtensions { get; }

    Task<DocumentExtractionResult> ExtractAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default);
}