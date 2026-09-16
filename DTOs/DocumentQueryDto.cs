using System.ComponentModel.DataAnnotations;

namespace GradrTab.DTOs;

// Optional filters accepted by GET /api/v1/documents
public class DocumentQueryDto
{
    // Pdf, Word, Image, Csv, Json or Text
    public string? Type { get; set; }

    // Pending, Processing, Completed or Failed
    public string? Status { get; set; }

    // Free text matched against the file name and the extracted text
    public string? Search { get; set; }

    public DateTime? UploadedFrom { get; set; }

    public DateTime? UploadedTo { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 200)]
    public int PageSize { get; set; } = 20;
}
