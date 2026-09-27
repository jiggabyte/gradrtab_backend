using System.Security.Claims;
using GradrTab.DTOs;
using GradrTab.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GradrTab.Controllers;

// Receives the rubrics the frontend builds and stores them, together with the
// point totals the server derives from the submitted levels.
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
    // Body matches the rubric the frontend submits:
    // { "id": "rub_essay", "title": "Research Essay", "criteria": [ ... ] }
    // Submitting the same id again replaces the stored rubric, so the frontend
    // can safely resubmit after an edit.
    [HttpPost]
    public async Task<ActionResult<RubricResponseDto>> Submit(
        [FromBody] RubricSubmissionDto request,
        CancellationToken cancellationToken)
    {
        var result = await _rubricService.SubmitAsync(request, GetCurrentUserId(), cancellationToken);

        if (result.Succeeded && result.Rubric is not null)
        {
            _logger.LogInformation(
                "Rubric {RubricKey} stored with {MaxPoints} points across {Criteria} criteria",
                result.Rubric.RubricKey, result.Rubric.MaxPoints, result.Rubric.CriterionCount);

            return CreatedAtAction(nameof(GetById), new { id = result.Rubric.Id }, result.Rubric);
        }

        // Every problem in the payload is reported at once instead of making the
        // frontend resubmit to discover the next one
        return BadRequest(new { errors = result.Errors });
    }

    // GET /api/v1/rubrics/{id}
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RubricResponseDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var rubric = await _rubricService.GetByIdAsync(id, cancellationToken);

        return rubric is null
            ? NotFound(new { message = $"No rubric with id {id} was found." })
            : Ok(rubric);
    }

    // GET /api/v1/rubrics/by-key/{key}
    // Looks a rubric up by the id the frontend submitted, for example rub_essay
    [HttpGet("by-key/{key}")]
    public async Task<ActionResult<RubricResponseDto>> GetByKey(string key, CancellationToken cancellationToken)
    {
        var rubric = await _rubricService.GetByKeyAsync(key, GetCurrentUserId(), cancellationToken);

        return rubric is null
            ? NotFound(new { message = $"No rubric with the id '{key}' was found." })
            : Ok(rubric);
    }

    // GET /api/v1/rubrics
    [HttpGet]
    public async Task<ActionResult<PagedResultDto<RubricSummaryDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _rubricService.ListAsync(GetCurrentUserId(), page, pageSize, cancellationToken));
    }

    // DELETE /api/v1/rubrics/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var rubric = await _rubricService.GetByIdAsync(id, cancellationToken);

        if (rubric is null)
        {
            return NotFound(new { message = $"No rubric with id {id} was found." });
        }

        await _rubricService.DeleteAsync(id, GetCurrentUserId(), cancellationToken);

        _logger.LogInformation("Deleted rubric {RubricKey} ({RubricId})", rubric.RubricKey, id);

        return NoContent();
    }

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }
}
