using System.ComponentModel.DataAnnotations;

namespace GradrTab.DTOs;

// Body of POST /api/v1/submissions and PUT /api/v1/submissions/{id}. The
// grading state and the failure reason are left out on purpose, those belong to
// the grading pipeline and not to whoever files the submission.
public class SubmissionWriteDto
{
    // The rubric being graded, may be left out
    public Guid? RubricId { get; set; }

    // The uploaded document this submission was read from
    public Guid? DocumentId { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string StudentId { get; set; } = string.Empty;

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string StudentName { get; set; } = string.Empty;

    // The grading result, stored as jsonb
    [StringLength(200_000)]
    public string? Evaluation { get; set; }
}

// What the server stored, including the grading state the server owns. The
// evaluation comes back as the parsed object it was stored as, null while the
// submission has not been graded (or could not be read back).
public record SubmissionResponseDto(
    Guid Id,
    Guid? RubricId,
    Guid? DocumentId,
    string StudentId,
    string StudentName,
    EvaluationDto? Evaluation,
    string GradingStatus,
    string FailureReason,
    Guid? UploadedByUserId,
    DateTime CreatedAt,
    DateTime UpdatedAt);

// Compact entry for the list endpoint, without the evaluation
public record SubmissionSummaryDto(
    Guid Id,
    Guid? RubricId,
    Guid? DocumentId,
    string StudentId,
    string StudentName,
    string GradingStatus,
    DateTime CreatedAt,
    DateTime UpdatedAt);
