using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GradrTab.Models;

// A single unit of information that was read out of an uploaded document.
// CSV files produce one entry per row, JSON files one entry per record/property,
// PDF and Word files one entry per page/paragraph/table row.
public class DocumentEntry
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid DocumentId { get; set; }

    [ForeignKey(nameof(DocumentId))]
    public Document? Document { get; set; }

    // Zero based order in which the entry appeared inside the document
    public int EntryIndex { get; set; }

    // Page, Row, Record, Field, Paragraph or TableRow
    [Required]
    [MaxLength(40)]
    public string EntryType { get; set; } = string.Empty;

    // Optional label such as a csv column name or a json property name
    [MaxLength(255)]
    public string? Key { get; set; }

    // Plain text representation of the entry, used for previews and searching
    public string? Content { get; set; }

    // Structured payload of the entry kept as JSON
    [Column(TypeName = "jsonb")]
    public string? Data { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}