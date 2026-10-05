using GradrTab.DTOs;
using GradrTab.Services;
using Microsoft.AspNetCore.Http;

namespace GradrTab.Tests.Support;

// Stands in for the chat completion provider. It answers with a canned result
// and records every request, so a test can assert on what was asked and on what
// happened to the submission afterwards.
public sealed class FakeAiService : IAiService
{
    private readonly AiChatResult _result;

    public FakeAiService(AiChatResult result)
    {
        _result = result;
    }

    // Every message list that was sent, in order
    public List<IReadOnlyList<AiChatMessageDto>> Requests { get; } = [];

    public bool IsConfigured => _result.Succeeded || _result.StatusCode != StatusCodes.Status503ServiceUnavailable;

    public string? UnavailableReason => IsConfigured
        ? null
        : "No AI provider key is configured.";

    public string Model { get; init; } = "test/model";

    // Answers with this content as if the model had returned it
    public static FakeAiService AnsweringWith(string content) =>
        new(AiChatResult.Success(new AiChatResponseDto(
            content, "test/model", "stop", "gen-test", 120, 60, 180, false)));

    // Behaves like a provider that is down or refused the call
    public static FakeAiService Failing(string message, int statusCode) =>
        new(AiChatResult.Failure(message, statusCode: statusCode));

    public Task<AiChatResult> CompleteAsync(
        IReadOnlyList<AiChatMessageDto> messages,
        string? model = null,
        double? temperature = null,
        int? maxTokens = null,
        string? endUserId = null,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(messages);
        return Task.FromResult(_result);
    }
}
