using System.Security.Claims;
using GradrTab.Configuration;
using GradrTab.DTOs;
using GradrTab.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GradrTab.Controllers;

// Exposes the AI provider (OpenRouter by default) so the frontend can ask a
// question without holding a provider key of its own.
[ApiController]
[Authorize]
[Route("api/v1/ai")]
public class AiController : ControllerBase
{
    private readonly IAiService _aiService;
    private readonly AiOptions _options;
    private readonly ILogger<AiController> _logger;

    public AiController(
        IAiService aiService,
        IOptions<AiOptions> options,
        ILogger<AiController> logger)
    {
        _aiService = aiService;
        _options = options.Value;
        _logger = logger;
    }

    // POST /api/v1/ai/chat
    // Body: { "prompt": "What is the meaning of life?" }
    // Messages may be sent instead of prompt to keep a conversation, and
    // systemPrompt is prepended as a system message.
    [HttpPost("chat")]
    public async Task<ActionResult<AiChatResponseDto>> Chat(
        [FromBody] AiChatRequestDto request,
        CancellationToken cancellationToken)
    {
        var messages = BuildMessages(request);

        if (messages is null)
        {
            return BadRequest(new
            {
                message = "Send a prompt or at least one message, for example { \"prompt\": \"What is the meaning of life?\" }."
            });
        }

        var result = await _aiService.CompleteAsync(
            messages,
            request.Model,
            request.Temperature,
            request.MaxTokens,
            GetCurrentUserId(),
            cancellationToken);

        if (result.Succeeded && result.Response is not null)
        {
            return Ok(result.Response);
        }

        // 503 when no key is configured, 400 for a rejected prompt and 502/504
        // when the provider itself is the problem
        var statusCode = result.StatusCode ?? StatusCodes.Status502BadGateway;

        _logger.LogWarning("AI chat request failed with {StatusCode}: {Error}", statusCode, result.ErrorMessage);

        return StatusCode(statusCode, new AiChatErrorDto(
            result.ErrorMessage ?? "The AI provider could not be reached.",
            result.ProviderCode,
            result.StatusCode,
            result.ProviderDetail,
            result.RetryAfterSeconds));
    }

    // GET /api/v1/ai/status
    // Lets the frontend hide the AI features while no key is configured. It
    // deliberately reports no secret.
    [HttpGet("status")]
    public ActionResult<AiStatusResponseDto> Status() => Ok(new AiStatusResponseDto(
        _aiService.IsConfigured,
        _aiService.UnavailableReason,
        _aiService.Model,
        _options.BaseUrl,
        !string.IsNullOrWhiteSpace(_options.SiteUrl) || !string.IsNullOrWhiteSpace(_options.SiteName)));

    // Turns the shorthand body into the message list the provider expects.
    // Returns null when the request carries nothing to send.
    private static List<AiChatMessageDto>? BuildMessages(AiChatRequestDto request)
    {
        var messages = new List<AiChatMessageDto>();

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new AiChatMessageDto("system", request.SystemPrompt));
        }

        if (request.Messages is { Count: > 0 })
        {
            messages.AddRange(request.Messages.Where(message => !string.IsNullOrWhiteSpace(message?.Content)));
        }
        else if (!string.IsNullOrWhiteSpace(request.Prompt))
        {
            messages.Add(new AiChatMessageDto("user", request.Prompt));
        }

        return messages.Count > 0 ? messages : null;
    }

    // Sent to the provider as the "user" field so it can rate limit per account user
    private string? GetCurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
}
