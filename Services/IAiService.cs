using GradrTab.DTOs;

namespace GradrTab.Services;

// Talks to a chat completion provider (OpenRouter by default).
// Problems are reported on the returned result instead of being thrown, so a
// provider outage cannot take down the calling request.
public interface IAiService
{
    // False when no API key is available, the service then refuses every call
    bool IsConfigured { get; }

    // Human readable explanation of why IsConfigured is false
    string? UnavailableReason { get; }

    // Model used when a request does not name one
    string Model { get; }

    Task<AiChatResult> CompleteAsync(
        IReadOnlyList<AiChatMessageDto> messages,
        string? model = null,
        double? temperature = null,
        int? maxTokens = null,
        string? endUserId = null,
        CancellationToken cancellationToken = default);
}

// Outcome of one chat completion, exactly one of Response / ErrorMessage is set.
// ProviderDetail carries what the provider said about the failure, a bare
// "Provider returned error" is not enough to act on.
public sealed record AiChatResult(
    bool Succeeded,
    AiChatResponseDto? Response,
    string? ErrorMessage,
    int? ProviderCode,
    int? StatusCode,
    string? ProviderDetail = null,
    int? RetryAfterSeconds = null)
{
    public static AiChatResult Success(AiChatResponseDto response) =>
        new(true, response, null, null, 200);

    public static AiChatResult Failure(
        string message,
        int? providerCode = null,
        int? statusCode = null,
        string? providerDetail = null,
        int? retryAfterSeconds = null) =>
        new(false, null, message, providerCode, statusCode, providerDetail, retryAfterSeconds);
}
