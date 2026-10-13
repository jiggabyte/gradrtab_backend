using GradrTab.DTOs;
using GradrTab.Services;
using GradrTab.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GradrTab.Tests;

public class AiDetectionTests
{
    private const string GoodAnswer = "{ \"aiPercentage\": 72, \"humanPercentage\": 28, \"verdict\": \"likely-ai\", \"confidence\": 68, \"summary\": \"Uniform cadence.\", \"signals\": [{ \"signal\": \"Uniform cadence\", \"detail\": \"Even sentences.\", \"weight\": 40 }] }";

    [Fact]
    public async Task DetectForDocument_ReturnsTheAiPercentage()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var seeded = TestHelpers.SeedGradableSubmission(uow, userId);
        var docId = seeded.Submission.DocumentId!.Value;
        var ai = FakeAiService.AnsweringWith(GoodAnswer);
        var service = TestHelpers.CreateAiDetectionService(uow, ai);

        var result = await service.DetectForDocumentAsync(docId, new AiDetectionRequestDto(), userId);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(72, result.Detection!.AiPercentage);
        Assert.Equal(28, result.Detection.HumanPercentage);
        Assert.Equal("likely-ai", result.Detection.Verdict);
        Assert.Equal(docId, result.Detection.DocumentId);
    }

    [Fact]
    public async Task Detect_SendsTextWithTheSystemPrompt()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var seeded = TestHelpers.SeedGradableSubmission(uow, userId);
        var ai = FakeAiService.AnsweringWith(GoodAnswer);
        var service = TestHelpers.CreateAiDetectionService(uow, ai);

        await service.DetectForSubmissionAsync(seeded.SubmissionId, new AiDetectionRequestDto(), userId);

        var messages = Assert.Single(ai.Requests);
        Assert.Equal("system", messages[0].Role);
        Assert.Contains("forensic linguist", messages[0].Content);
        Assert.Contains("aiPercentage", messages[0].Content);
        Assert.Equal("user", messages[1].Role);
    }

    [Fact]
    public async Task Detect_FillsInTheVerdictFromThePercentage()
    {
        var uow = new InMemoryUnitOfWork();
        var ai = FakeAiService.AnsweringWith("{ \"aiPercentage\": 12 }");
        var service = TestHelpers.CreateAiDetectionService(uow, ai);

        var result = await service.DetectForTextAsync(new string('h', 500), new AiDetectionRequestDto(), Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.Equal("likely-human", result.Detection!.Verdict);
        Assert.Equal(88, result.Detection.HumanPercentage);
    }

    [Fact]
    public async Task Detect_RefusesTextThatIsTooShort()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateAiDetectionService(uow, FakeAiService.AnsweringWith(GoodAnswer));

        var result = await service.DetectForTextAsync("Too short.", new AiDetectionRequestDto(), Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, result.StatusCode);
    }

    [Fact]
    public async Task Detect_AnswersNotFoundForAnotherOwnersDocument()
    {
        var uow = new InMemoryUnitOfWork();
        var doc = TestHelpers.SeedDocument(uow, Guid.NewGuid(), text: new string('x', 500));
        var service = TestHelpers.CreateAiDetectionService(uow, FakeAiService.AnsweringWith(GoodAnswer));

        var result = await service.DetectForDocumentAsync(doc.Id, new AiDetectionRequestDto(), Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
    }


    [Fact]
    public async Task Detect_NormalisesPercentagesThatDoNotAddUp()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var seeded = TestHelpers.SeedGradableSubmission(uow, userId);
        var ai = FakeAiService.AnsweringWith("{ \"aiPercentage\": 80, \"humanPercentage\": 50 }");
        var service = TestHelpers.CreateAiDetectionService(uow, ai);

        var result = await service.DetectForSubmissionAsync(seeded.SubmissionId, new AiDetectionRequestDto(), userId);

        Assert.True(result.Succeeded);
        Assert.Equal(80, result.Detection!.AiPercentage);
        Assert.Equal(20, result.Detection.HumanPercentage);
    }

    [Fact]
    public async Task Detect_PassesProviderFailuresThrough()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateAiDetectionService(
            uow, FakeAiService.Failing("No credits left.", StatusCodes.Status402PaymentRequired));

        var result = await service.DetectForTextAsync(new string('y', 500), new AiDetectionRequestDto(), Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status402PaymentRequired, result.StatusCode);
    }

    [Fact]
    public async Task SubmissionEndpoint_ReturnsTheDetection()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var seeded = TestHelpers.SeedGradableSubmission(uow, userId);
        var controller = TestHelpers.CreateSubmissionsController(uow, FakeAiService.AnsweringWith(GoodAnswer), userId);

        var result = await controller.DetectAi(seeded.SubmissionId, null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(72, Assert.IsType<AiDetectionResponseDto>(ok.Value).AiPercentage);
    }

    [Fact]
    public async Task TextEndpoint_RejectsAnEmptyBody()
    {
        var controller = TestHelpers.CreateAiController(FakeAiService.AnsweringWith(GoodAnswer), userId: Guid.NewGuid());

        var result = await controller.Detect(null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
