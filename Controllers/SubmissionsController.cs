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
    private readonly ILogger<SubmissionsController> _logger;

    public SubmissionsController(
        ISubmissionService submissionService,
        IGradingService gradingService,
        ILogger<SubmissionsController> logger)
    {
        _submissionService = submissionService;
        _gradingService = gradingService;
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
