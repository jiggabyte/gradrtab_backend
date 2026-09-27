using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GradrTab.Models;

// A file that was uploaded through the multipart/form-data endpoint together
// with everything that was read out of it.
public class Document
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    // The file name as sent by the client (always stripped of any path information)
    [Required]
    [MaxLength(500)]
    public string OriginalFileName { get; set; } = string.Empty;

    // The generated name under which the file is kept on disk
    [Required]
    [MaxLength(255)]
    public string StoredFileName { get; set; } = string.Empty;

    // Location of the stored copy relative to the configured storage root
    [Required]
    [MaxLength(1000)]
    public string StoragePath { get; set; } = string.Empty;

    [MaxLength(150)]
    public string ContentType { get; set; } = string.Empty;

    [MaxLength(20)]
    public string FileExtension { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public DocumentType DocumentType { get; set; } = DocumentType.Unknown;

    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;

    // The plain text that was read out of the document, null when nothing could be read
    public string? ExtractedText { get; set; }

    public int CharacterCount { get; set; }

    public int WordCount { get; set; }

    // Pages for PDFs / Word documents, null when the format does not report it
    public int? PageCount { get; set; }

    // Number of structured rows/records that were stored in DocumentEntries
    public int EntryCount { get; set; }

    // Type specific details (image dimensions, csv columns, pdf page size ...) kept as JSON
    [Column(TypeName = "jsonb")]
    public string? Metadata { get; set; }

    // Non fatal notes raised while processing, e.g. truncated rows or OCR not available
    [Column(TypeName = "jsonb")]
    public string? Warnings { get; set; }

    public string? ErrorMessage { get; set; }

    // The authenticated user that uploaded the file, null for anonymous uploads
    public Guid? UploadedByUserId { get; set; }

    [ForeignKey(nameof(UploadedByUserId))]
    public User? UploadedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAt { get; set; }

    public ICollection<DocumentEntry> Entries { get; set; } = new List<DocumentEntry>();
}