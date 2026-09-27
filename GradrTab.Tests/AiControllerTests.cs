using System.Net;
using System.Text.Json;
using GradrTab.DTOs;
using GradrTab.Tests.Support;
using Microsoft.AspNetCore.Mvc;

namespace GradrTab.Tests;

public class AiControllerTests
{
    private const string AnswerBody = """
        {
          "id": "gen-1",
          "model": "openai/gpt-4o-mini",
          "choices": [
            { "message": { "role": "assistant", "content": "Forty two." }, "finish_reason": "stop" }
          ],
          "usage": { "prompt_tokens": 8, "completion_tokens": 3, "total_tokens": 11 }
        }
        """;

    [Fact]
    public async Task Chat_TurnsAPromptIntoASingleUserMessage()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, AnswerBody);
        var controller = TestHelpers.CreateAiController(
            TestHelpers.CreateAiService(handler), userId: Guid.NewGuid());

        var result = await controller.Chat(
            new AiChatRequestDto { Prompt = "What is the meaning of life?" },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AiChatResponseDto>(ok.Value);
        Assert.Equal("Forty two.", response.Content);

        using var sent = JsonDocument.Parse(handler.LastBody!);
        var messages = sent.RootElement.GetProperty("messages");

        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        Assert.Equal("What is the meaning of life?", messages[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Chat_PutsTheSystemPromptInFrontOfTheConversation()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, AnswerBody);
        var controller = TestHelpers.CreateAiController(TestHelpers.CreateAiService(handler));

        await controller.Chat(
            new AiChatRequestDto
            {
                SystemPrompt = "You are a grader.",
                Messages = [new AiChatMessageDto("user", "Grade this.")],
            },
            CancellationToken.None);

        using var sent = JsonDocument.Parse(handler.LastBody!);
        var messages = sent.RootElement.GetProperty("messages");

        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("You are a grader.", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
    }

    [Fact]
    public async Task Chat_SendsTheSignedInUserAsTheProviderUser()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, AnswerBody);
        var userId = Guid.NewGuid();
        var controller = TestHelpers.CreateAiController(
            TestHelpers.CreateAiService(handler), userId: userId);

        await controller.Chat(new AiChatRequestDto { Prompt = "hi" }, CancellationToken.None);

        using var sent = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(userId.ToString(), sent.RootElement.GetProperty("user").GetString());
    }

    [Fact]
    public async Task Chat_ReturnsBadRequestWhenNothingWasSent()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, AnswerBody);
        var controller = TestHelpers.CreateAiController(TestHelpers.CreateAiService(handler));

        var result = await controller.Chat(new AiChatRequestDto(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task Chat_PassesTheProviderStatusThroughOnFailure()
    {
        const string errorBody = """{ "error": { "code": 402, "message": "No credits left." } }""";

        var handler = new StubHttpMessageHandler(HttpStatusCode.PaymentRequired, errorBody);
        var controller = TestHelpers.CreateAiController(TestHelpers.CreateAiService(handler));

        var result = await controller.Chat(new AiChatRequestDto { Prompt = "hi" }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(402, objectResult.StatusCode);

        var error = Assert.IsType<AiChatErrorDto>(objectResult.Value);
        Assert.Equal("No credits left.", error.Message);
        Assert.Equal(402, error.ProviderCode);
    }

    [Fact]
    public void Status_ReportsTheConfigurationWithoutLeakingTheKey()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, AnswerBody);
        var controller = TestHelpers.CreateAiController(TestHelpers.CreateAiService(handler));

        var result = controller.Status();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var status = Assert.IsType<AiStatusResponseDto>(ok.Value);

        Assert.True(status.IsConfigured);
        Assert.Null(status.UnavailableReason);
        Assert.Equal("openai/gpt-4o-mini", status.Model);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", status.BaseUrl);
        Assert.True(status.SendsSiteHeaders);
        Assert.DoesNotContain("test-key", JsonSerializer.Serialize(status));
    }
}
