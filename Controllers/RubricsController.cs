using System.Security.Claims;
using GradrTab.DTOs;
using GradrTab.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GradrTab.Controllers;

// Creates, reads, updates and deletes the rubrics of the signed in user. Every
// route is scoped to that user, a rubric owned by somebody else reads as a 404.
[ApiController]
[Authorize]
[Route("api/v1/rubrics")]
public class RubricsController : ControllerBase
{
    private readonly IRubricService _rubricService;
    private readonly ILogger<RubricsController> _logger;

    public RubricsController(IRubricService rubricService, ILogger<RubricsController> logger)
    {
        _rubricService = rubricService;
        _logger = logger;
    }

    // POST /api/v1/rubrics
    // Body: { "courseName": "Economics", "courseId": "ECON-101", "rubricContent": "..." }
    // The server adds the id, the owner and the timestamps.
    [HttpPost]
    public async Task<ActionResult<RubricResponseDto>> Create(
        [FromBody] RubricWriteDto request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var rubric = await _rubricService.CreateAsync(request, userId.Value, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = rubric.Id }, rubric);
    }

    // GET /api/v1/rubrics/{id}
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RubricResponseDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var rubric = await _rubricService.GetByIdAsync(id, userId.Value, cancellationToken);

        return rubric is null
            ? NotFound(new { message = $"No rubric with id {id} was found." })
            : Ok(rubric);
    }

    // GET /api/v1/rubrics?page=1&pageSize=20
    // The caller's own rubrics, newest first, without the rubric content.
    [HttpGet]
    public async Task<ActionResult<PagedResultDto<RubricSummaryDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        return Ok(await _rubricService.ListAsync(userId.Value, page, pageSize, cancellationToken));
    }

    // PUT /api/v1/rubrics/{id}
    // Replaces the course and the content of one rubric, the id, the owner and
    // the creation time stay as they are.
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RubricResponseDto>> Update(
        Guid id,
        [FromBody] RubricWriteDto request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await _rubricService.UpdateAsync(id, request, userId.Value, cancellationToken);

        return result.Succeeded && result.Rubric is not null
            ? Ok(result.Rubric)
            : NotFound(new { message = $"No rubric with id {id} was found." });
    }

    // DELETE /api/v1/rubrics/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await _rubricService.DeleteAsync(id, userId.Value, cancellationToken);

        if (!result.Succeeded || result.Rubric is null)
        {
            return NotFound(new { message = $"No rubric with id {id} was found." });
        }

        _logger.LogInformation("User {UserId} deleted rubric {RubricId}", userId.Value, id);

        return NoContent();
    }

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }
}
