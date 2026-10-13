using System.ComponentModel.DataAnnotations;

namespace GradrTab.DTOs;

// Body of POST /api/v1/submissions/{id}/email-grading-result.
// Takes the grader's email address; the submission id comes from the route.
public class GradingEmailRequestDto
{
    // Where the grading result goes. The mail carries the submission details,
    // the rubric details and the evaluation.
    [Required]
    [EmailAddress]
    [StringLength(320)]
    public string GraderEmail { get; set; } = string.Empty;
}

// What the endpoint answers with once the mail was accepted by the SMTP server.
public record GradingEmailResponseDto(
    string ToEmail,
    Guid SubmissionId,
    string Message);
