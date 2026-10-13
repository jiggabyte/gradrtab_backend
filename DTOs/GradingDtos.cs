using System.ComponentModel.DataAnnotations;

namespace GradrTab.DTOs;

// Body of POST /api/v1/submissions/{id}/grade. Every field is optional, an empty
// body grades with whatever the Ai section is configured with.
public class GradingRequestDto
{
    // Grades with this model instead of the configured Ai:Model
    [StringLength(200)]
    public string? Model { get; set; }

    // Extra guidance for this one grading, for example "weigh the sources heavily"
    [StringLength(4_000)]
    public string? Instructions { get; set; }

    [Range(0.0, 2.0)]
    public double? Temperature { get; set; }

    [Range(1, 128_000)]
    public int? MaxTokens { get; set; }
}

// One line of the rubric as it was scored
public record CriterionEvaluationDto(
    string Name,
    double? Score,
    double? MaxScore,
    string? Comments);

// The grade that came out of it. This is also what is stored on the submission,
// so reading the submission back hands out the same evaluation.
public record EvaluationDto(
    Guid SubmissionId,
    Guid? RubricId,
    Guid? DocumentId,
    string StudentId,
    string StudentName,
    string CourseName,
    string CourseId,
    double? OverallScore,
    double? MaxScore,
    double? Percentage,
    string? Grade,
    string? Summary,
    IReadOnlyList<CriterionEvaluationDto> Criteria,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Improvements,
    // True when the assessment was too long to send whole and was cut short
    bool AssessmentTruncated,
    string Model,
    DateTime EvaluatedAt);
