using System.Net;
using System.Text;
using System.Text.Json;
using GradrTab.DTOs;
using GradrTab.Services;
using GradrTab.Tests.Support;

namespace GradrTab.Tests;

public class AiServiceTests
{
    private const string SuccessBody = """
        {
          "id": "gen-abc123",
          "object": "chat.completion",
          "model": "openai/gpt-4o-mini-2024-07-18",
          "choices": [
            {
              "index": 0,
              "message": { "role": "assistant", "content": "Forty two." },
              "finish_reason": "stop"
            }
          ],
          "usage": { "prompt_tokens": 12, "completion_tokens": 5, "total_tokens": 17 }
        }
        """;

    [Fact]
    public async Task CompleteAsync_SendsTheDocumentedRequestAndReadsTheAnswer()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var service = TestHelpers.CreateAiService(handler);

        var result = await service.CompleteAsync(
            [new AiChatMessageDto("user", "What is the meaning of life?")],
            endUserId: "user-7");

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Response);
        Assert.Equal("Forty two.", result.Response.Content);
        Assert.Equal("openai/gpt-4o-mini-2024-07-18", result.Response.Model);
        Assert.Equal("stop", result.Response.FinishReason);
        Assert.Equal("gen-abc123", result.Response.CompletionId);
        Assert.Equal(12, result.Response.PromptTokens);
        Assert.Equal(5, result.Response.CompletionTokens);
        Assert.Equal(17, result.Response.TotalTokens);
        Assert.False(result.Response.Truncated);

        // Method, endpoint and headers match the documented OpenRouter call
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("Bearer test-key", handler.LastAuthorization);
        Assert.Equal("https://gradrtab.example", handler.LastHeader("HTTP-Referer"));
        Assert.Equal("GradrTab", handler.LastHeader("X-OpenRouter-Title"));
        Assert.Equal("application/json", handler.LastRequest.Content!.Headers.ContentType!.MediaType);

        using var sent = JsonDocument.Parse(handler.LastBody!);
        var root = sent.RootElement;

        Assert.Equal("openai/gpt-4o-mini", root.GetProperty("model").GetString());
        Assert.Equal(100, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal("user-7", root.GetProperty("user").GetString());

        var message = root.GetProperty("messages")[0];
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.Equal("What is the meaning of life?", message.GetProperty("content").GetString());
    }

    [Fact]
    public async Task CompleteAsync_SurfacesTheUpstreamReasonBehindAProviderError()
    {
        // The shape OpenRouter returns when the upstream provider rate limits:
        // a generic message, with the real cause only in error.metadata
        const string body = """
            {
              "error": {
                "code": 429,
                "message": "Provider returned error",
                "metadata": {
                  "provider_name": "OpenAI",
                  "provider_code": 429,
                  "error_type": "rate_limit_exceeded",
                  "raw": "Rate limit reached for gpt-6-sol"
                }
              }
            }
            """;

        var handler = new StubHttpMessageHandler(HttpStatusCode.TooManyRequests, body);
        var service = TestHelpers.CreateAiService(handler);

        var result = await service.CompleteAsync([new AiChatMessageDto("user", "hello")]);

        Assert.False(result.Succeeded);
        Assert.Equal(429, result.StatusCode);

        // Without this the caller only ever sees "Provider returned error"
        Assert.NotNull(result.ProviderDetail);
        Assert.Contains("provider=OpenAI", result.ProviderDetail);
        Assert.Contains("providerCode=429", result.ProviderDetail);
        Assert.Contains("errorType=rate_limit_exceeded", result.ProviderDetail);
        Assert.Contains("Rate limit reached for gpt-6-sol", result.ProviderDetail);
    }

    [Fact]
    public async Task CompleteAsync_ReportsTheRetryAfterHint()
    {
        const string body = """{ "error": { "code": 429, "message": "Provider returned error" } }""";

        var handler = new StubHttpMessageHandler(
            _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                response.Headers.TryAddWithoutValidation("Retry-After", "30");
                return response;
            });

        var service = TestHelpers.CreateAiService(handler);

        var result = await service.CompleteAsync([new AiChatMessageDto("user", "hello")]);

        // 30 seconds is what the caller should wait before trying again
        Assert.Equal(30, result.RetryAfterSeconds);
    }

    [Fact]
    public async Task CompleteAsync_StillWorksWhenTheBodyCarriesNoMetadata()
    {
        const string body = """{ "error": { "code": 402, "message": "No credits left." } }""";

        var handler = new StubHttpMessageHandler(HttpStatusCode.PaymentRequired, body);
        var service = TestHelpers.CreateAiService(handler);

        var result = await service.CompleteAsync([new AiChatMessageDto("user", "hello")]);

        Assert.Equal("No credits left.", result.ErrorMessage);
        Assert.Equal(402, result.ProviderCode);
        Assert.Null(result.ProviderDetail);
    }

    [Fact]
    public async Task CompleteAsync_ReportsTheProviderErrorMessage()
    {
        const string errorBody = """
            { "error": { "code": 402, "message": "This request requires more credits." } }
            """;

        var handler = new StubHttpMessageHandler(HttpStatusCode.PaymentRequired, errorBody);
        var service = TestHelpers.CreateAiService(handler);

        var result = await service.CompleteAsync([new AiChatMessageDto("user", "hello")]);

        Assert.False(result.Succeeded);
        Assert.Null(result.Response);
        Assert.Equal("This request requires more credits.", result.ErrorMessage);
        Assert.Equal(402, result.ProviderCode);
        Assert.Equal(402, result.StatusCode);
    }

    [Fact]
    public async Task CompleteAsync_FallsBackToTheStatusWhenTheBodyIsNotTheErrorShape()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.TooManyRequests, "slow down");
        var service = TestHelpers.CreateAiService(handler);

        var result = await service.CompleteAsync([new AiChatMessageDto("user", "hello")]);

        Assert.False(result.Succeeded);
        Assert.Contains("rate limiting", result.ErrorMessage);
        Assert.Contains("slow down", result.ErrorMessage);
        Assert.Equal(429, result.StatusCode);
    }

    [Fact]
    public async Task CompleteAsync_ReportsAnUnreadableSuccessBody()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "<html>maintenance</html>", "text/html");
        var service = TestHelpers.CreateAiService(handler);

        var result = await service.CompleteAsync([new AiChatMessageDto("user", "hello")]);

        Assert.False(result.Succeeded);
        Assert.Equal(502, result.StatusCode);
    }

    [Fact]
    public async Task CompleteAsync_FlagsAnAnswerThatHitTheTokenLimit()
    {
        const string body = """
            {
              "id": "gen-trunc",
              "model": "openai/gpt-4o-mini",
              "choices": [
                { "message": { "role": "assistant", "content": "part" }, "finish_reason": "length" }
              ],
              "usage": { "prompt_tokens": 3, "completion_tokens": 4, "total_tokens": 7 }
            }
            """;

        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, body);
        var service = TestHelpers.CreateAiService(handler);

        var result = await service.CompleteAsync([new AiChatMessageDto("user", "hello")], maxTokens: 4);

        Assert.True(result.Succeeded);
        Assert.True(result.Response!.Truncated);
        Assert.Equal(4, result.Response.CompletionTokens);
    }

    [Fact]
    public async Task CompleteAsync_UsesTheRequestedModelAndDropsUnknownRoles()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var service = TestHelpers.CreateAiService(handler);

        await service.CompleteAsync(
            [new AiChatMessageDto("Moderator", "be brief")],
            model: "anthropic/claude-3.5-sonnet",
            temperature: 0.2,
            maxTokens: 64);

        using var sent = JsonDocument.Parse(handler.LastBody!);

        Assert.Equal("anthropic/claude-3.5-sonnet", sent.RootElement.GetProperty("model").GetString());
        Assert.Equal(0.2, sent.RootElement.GetProperty("temperature").GetDouble());
        Assert.Equal(64, sent.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal("user", sent.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task CompleteAsync_RefusesAnEmptyPromptWithoutCallingTheProvider()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var service = TestHelpers.CreateAiService(handler);

        var result = await service.CompleteAsync([]);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.StatusCode);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task CompleteAsync_RefusesAPromptOverTheConfiguredLimit()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
        var service = TestHelpers.CreateAiService(handler);

        var result = await service.CompleteAsync([new AiChatMessageDto("user", new string('x', 1_001))]);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("The prompt is 1001 characters long, the maximum is 1000.", result.ErrorMessage);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task CompleteAsync_RefusesToCallTheProviderWithoutAKey()
    {
        var previous = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");

        try
        {
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", null);

            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, SuccessBody);
            var service = TestHelpers.CreateAiService(handler, TestHelpers.DefaultAiOptions(apiKey: null));

            Assert.False(service.IsConfigured);
            Assert.NotNull(service.UnavailableReason);

            var result = await service.CompleteAsync([new AiChatMessageDto("user", "hello")]);

            Assert.False(result.Succeeded);
            Assert.Equal(503, result.StatusCode);
            Assert.Null(handler.LastRequest);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", previous);
        }
    }
}
