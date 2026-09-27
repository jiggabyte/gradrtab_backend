namespace GradrTab.DTOs;

// A single piece of information that was read out of an uploaded document
public record DocumentEntryResponseDto(
    Guid Id,
    int EntryIndex,
    string EntryType,
    string? Key,
    string? Content,
    Dictionary<string, object?>? Data,
    DateTime CreatedAt);

// Summary of an uploaded document, returned by the list and get endpoints
public record DocumentResponseDto(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    string FileExtension,
    long FileSizeBytes,
    string DocumentType,
    string Status,
    int CharacterCount,
    int WordCount,
    int? PageCount,
    int EntryCount,
    Dictionary<string, object?>? Metadata,
    IReadOnlyList<string> Warnings,
    string? ErrorMessage,
    Guid? UploadedByUserId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ProcessedAt);

// The document together with the information that was extracted from it
public record DocumentDetailResponseDto(
    DocumentResponseDto Document,
    IReadOnlyList<DocumentEntryResponseDto> Entries);

// Outcome of processing one uploaded file, failures are reported per file so a
// batch upload can partially succeed
public record DocumentUploadResultDto(
    Guid? Id,
    string OriginalFileName,
    long FileSizeBytes,
    string Status,
    string? DocumentType,
    string? ErrorMessage,
    IReadOnlyList<string> Warnings);

public record DocumentUploadResponseDto(
    int AcceptedCount,
    int FailedCount,
    IReadOnlyList<DocumentUploadResultDto> Results);

// Pagination envelope shared by the list endpoints
public record PagedResultDto<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
