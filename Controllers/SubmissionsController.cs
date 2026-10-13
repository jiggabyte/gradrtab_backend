using System.Security.Claims;
using GradrTab.DTOs;
using GradrTab.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GradrTab.Controllers;

// Creates, reads, updates and deletes the submissions of the signed in user.
// Every route is scoped to that user, a submission owned by somebody else reads
// as a 404.
[ApiController]
[Authorize]
[Route("api/v1/submissions")]
public class SubmissionsController : ControllerBase
{
    private readonly ISubmissionService _submissionService;
    private readonly IGradingService _gradingService;
    private readonly IGradingEmailService _gradingEmailService;
    private readonly IAiDetectionService _detectionService;
    private readonly ILogger<SubmissionsController> _logger;

    public SubmissionsController(
        ISubmissionService submissionService,
        IGradingService gradingService,
        IGradingEmailService gradingEmailService,
        IAiDetectionService detectionService,
        ILogger<SubmissionsController> logger)
    {
        _submissionService = submissionService;
        _gradingService = gradingService;
        _gradingEmailService = gradingEmailService;
        _detectionService = detectionService;
        _logger = logger;
    }

    // POST /api/v1/submissions
    // Body: { "rubricId": "....", "documentId": "....", "studentId": "S-1", "studentName": "Ada" }
    // The server adds the id, the owner, the grading state and the timestamps.
    [HttpPost]
    public async Task<ActionResult<SubmissionResponseDto>> Create(
        [FromBody] SubmissionWriteDto request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var submission = await _submissionService.CreateAsync(request, userId.Value, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = submission.Id }, submission);
    }

    // GET /api/v1/submissions/{id}
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SubmissionResponseDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var submission = await _submissionService.GetByIdAsync(id, userId.Value, cancellationToken);

        return submission is null
            ? NotFound(new { message = $"No submission with id {id} was found." })
            : Ok(submission);
    }

    // GET /api/v1/submissions?page=1&pageSize=20&rubricId=...
    // The caller's own submissions, newest first, without the evaluation.
    // Passing rubricId narrows the list down to that one rubric.
    [HttpGet]
    public async Task<ActionResult<PagedResultDto<SubmissionSummaryDto>>> List(
        [FromQuery] Guid? rubricId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        return Ok(await _submissionService.ListAsync(userId.Value, rubricId, page, pageSize, cancellationToken));
    }

    // PUT /api/v1/submissions/{id}
    // Replaces the student, the rubric, the document and the evaluation of one
    // submission. The id, the owner, the grading state and the creation time
    // stay as they are.
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SubmissionResponseDto>> Update(
        Guid id,
        [FromBody] SubmissionWriteDto request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await _submissionService.UpdateAsync(id, request, userId.Value, cancellationToken);

        return result.Succeeded && result.Submission is not null
            ? Ok(result.Submission)
            : NotFound(new { message = $"No submission with id {id} was found." });
    }

    // POST /api/v1/submissions/{id}/grade
    // Grades the uploaded assessment against the rubric the submission was
    // filed with, stores the evaluation on the submission and returns it.
    // The body is optional, it only carries overrides:
    // { "model": "...", "instructions": "weigh the sources heavily" }
    // Answered with 422 when the submission has no rubric or no readable
    // document to grade, and with 503/502 when the AI provider is the problem.
    [HttpPost("{id:guid}/grade")]
    public async Task<ActionResult<EvaluationDto>> Grade(
        Guid id,
        [FromBody] GradingRequestDto? request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await _gradingService.GradeAsync(
            id,
            request ?? new GradingRequestDto(),
            userId.Value,
            cancellationToken);

        if (result.Succeeded && result.Evaluation is not null)
        {
            return Ok(result.Evaluation);
        }

        _logger.LogWarning(
            "Grading submission {SubmissionId} was refused with {StatusCode}: {Error}",
            id, result.StatusCode, result.ErrorMessage);

        return StatusCode(
            result.StatusCode ?? StatusCodes.Status422UnprocessableEntity,
            new { message = result.ErrorMessage });
    }

    // POST /api/v1/submissions/{id}/ai-detection
    // Sends the assessment text behind the submission to the AI and answers
    // with the estimated AI share. The body is optional, it only carries
    // overrides: { "model": "...", "instructions": "..." }
    [HttpPost("{id:guid}/ai-detection")]
    public async Task<ActionResult<AiDetectionResponseDto>> DetectAi(
        Guid id,
        [FromBody] AiDetectionRequestDto? request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await _detectionService.DetectForSubmissionAsync(
            id,
            request ?? new AiDetectionRequestDto(),
            userId.Value,
            cancellationToken);

        if (result.Succeeded && result.Detection is not null)
        {
            return Ok(result.Detection);
        }

        _logger.LogWarning(
            "AI detection for submission {SubmissionId} was refused with {Status}: {Error}",
            id, result.StatusCode, result.ErrorMessage);

        return StatusCode(
            result.StatusCode ?? StatusCodes.Status422UnprocessableEntity,
            new { message = result.ErrorMessage });
    }

    // POST /api/v1/submissions/{id}/email-grading-result
    // Mails the stored grading result of the submission to a grader.
    // Body: { "graderEmail": "grader@example.com" }
    // The mail carries the submission details, the rubric details and the
    // evaluation. Answered with 400 for a bad address, 404 for a missing
    // submission, 422 when there is no grade to send yet, 503 when SMTP is
    // not configured and 502 when the SMTP server refused the message.
    [HttpPost("{id:guid}/email-grading-result")]
    public async Task<IActionResult> EmailGradingResult(
        Guid id,
        [FromBody] GradingEmailRequestDto? request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        // A missing body still fails model validation the same way, without a
        // NullReferenceException. [ApiController] answers 400 for it only when
        // the parameter is non-nullable, so handle null here explicitly.
        var graderEmail = request?.GraderEmail?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(graderEmail))
        {
            // ModelState already holds the [Required]/[EmailAddress] errors when
            // a body was sent; surface those instead of a generic message.
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            return BadRequest(new
            {
                message = "Send a valid grader email address, for example { \"graderEmail\": \"grader@example.com\" }."
            });
        }

        var result = await _gradingEmailService.SendGradingResultAsync(
            id,
            graderEmail,
            userId.Value,
            cancellationToken);

        if (result.Succeeded && result.ToEmail is not null && result.SubmissionId is not null)
        {
            return Ok(new GradingEmailResponseDto(
                result.ToEmail,
                result.SubmissionId.Value,
                $"The grading result of submission {result.SubmissionId} was mailed to {result.ToEmail}."));
        }

        // 400 from model validation belongs to the request shape, everything
        // else (400 bad address / 404 / 422 / 503 / 502) comes from the service.
        if (!ModelState.IsValid && result.StatusCode == StatusCodes.Status400BadRequest)
        {
            return ValidationProblem(ModelState);
        }

        _logger.LogWarning(
            "Mailing grading result of submission {SubmissionId} to {Recipient} was refused with {StatusCode}: {Error}",
            id, graderEmail, result.StatusCode, result.ErrorMessage);

        return StatusCode(
            result.StatusCode ?? StatusCodes.Status502BadGateway,
            new { message = result.ErrorMessage });
    }

    // DELETE /api/v1/submissions/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await _submissionService.DeleteAsync(id, userId.Value, cancellationToken);

        if (!result.Succeeded || result.Submission is null)
        {
            return NotFound(new { message = $"No submission with id {id} was found." });
        }

        _logger.LogInformation("User {UserId} deleted submission {SubmissionId}", userId.Value, id);

        return NoContent();
    }

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }
}
