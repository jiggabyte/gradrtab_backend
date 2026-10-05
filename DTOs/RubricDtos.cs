using System.ComponentModel.DataAnnotations;

namespace GradrTab.DTOs;

// Body of POST /api/v1/rubrics and PUT /api/v1/rubrics/{id}. Creating and
// updating a rubric take the same fields, so one payload covers both.
public class RubricWriteDto
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string CourseName { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string CourseId { get; set; } = string.Empty;

    // The rubric itself, kept exactly as it was submitted
    [Required]
    [StringLength(100_000, MinimumLength = 1)]
    public string RubricContent { get; set; } = string.Empty;
}

// What the server stored, including the values the server owns itself
public record RubricResponseDto(
    Guid Id,
    string CourseName,
    string CourseId,
    string RubricContent,
    Guid? UploadedByUserId,
    DateTime CreatedAt,
    DateTime UpdatedAt);

// Compact entry for the list endpoint, without the rubric content itself
public record RubricSummaryDto(
    Guid Id,
    string CourseName,
    string CourseId,
    DateTime CreatedAt,
    DateTime UpdatedAt);
