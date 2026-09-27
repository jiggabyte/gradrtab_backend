using System.ComponentModel.DataAnnotations;

namespace GradrTab.DTOs;

// One message of a chat request, mirrors the OpenAI / OpenRouter message shape.
// Role is normally "user", "assistant" or "system".
public record AiChatMessageDto(string Role, string Content);

// Body of POST /api/v1/ai/chat.
// Send either Prompt (one user message) or Messages (a full conversation),
// SystemPrompt is prepended as a system message when both are used.
public class AiChatRequestDto
{
    [StringLength(200)]
    public string? Model { get; set; }

    [StringLength(20_000)]
    public string? Prompt { get; set; }

    [StringLength(20_000)]
    public string? SystemPrompt { get; set; }

    public List<AiChatMessageDto>? Messages { get; set; }

    [Range(0.0, 2.0)]
    public double? Temperature { get; set; }

    [Range(1, 128_000)]
    public int? MaxTokens { get; set; }
}

// The assistant answer plus the token accounting reported by the provider
public record AiChatResponseDto(
    string Content,
    string Model,
    string? FinishReason,
    string? CompletionId,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    bool Truncated);

// Returned instead of a response when the call could not be completed.
// ProviderDetail is the provider's own wording, for example which upstream
// provider rate limited, so the caller can tell a quota problem from a
// temporary one.
public record AiChatErrorDto(
    string Message,
    int? ProviderCode = null,
    int? StatusCode = null,
    string? ProviderDetail = null,
    int? RetryAfterSeconds = null);

// Reported by GET /api/v1/ai/status, deliberately contains no secret
public record AiStatusResponseDto(
    bool IsConfigured,
    string? UnavailableReason,
    string Model,
    string BaseUrl,
    bool SendsSiteHeaders);
