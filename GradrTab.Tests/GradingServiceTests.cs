using System.Text.Json;
using GradrTab.DTOs;
using GradrTab.Services;
using GradrTab.Tests.Support;
using Microsoft.AspNetCore.Http;

namespace GradrTab.Tests;

public class GradingServiceTests
{
    private const string GoodAnswer = """
        {
          "overallScore": 88,
          "maxScore": 100,
          "grade": "B+",
          "summary": "A clear argument carried by well analysed evidence.",
          "criteria": [
            { "name": "Thesis & Argument", "score": 35, "maxScore": 40, "comments": "The thesis is arguable and sustained." },
            { "name": "Evidence", "score": 36, "maxScore": 40, "comments": "Three case studies, all analysed." },
            { "name": "Mechanics", "score": 17, "maxScore": 20, "comments": "Citations correct, a few slips." }
          ],
          "strengths": ["Evidence is analysed rather than quoted."],
          "improvements": ["Signpost the third case study."]
        }
        """;

    [Fact]
    public async Task Grade_ReturnsTheEvaluationTheModelGave()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var seeded = TestHelpers.SeedGradableSubmission(uow, userId);
        var ai = FakeAiService.AnsweringWith(GoodAnswer);
        var service = TestHelpers.CreateGradingService(uow, ai);

        var result = await service.GradeAsync(seeded.SubmissionId, new GradingRequestDto(), userId);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var evaluation = result.Evaluation!;

        Assert.Equal(seeded.SubmissionId, evaluation.SubmissionId);
        Assert.Equal(88, evaluation.OverallScore);
        Assert.Equal(100, evaluation.MaxScore);
        Assert.Equal("B+", evaluation.Grade);
        Assert.Equal("A clear argument carried by well analysed evidence.", evaluation.Summary);
        Assert.Equal(3, evaluation.Criteria.Count);
        Assert.Equal("Evidence", evaluation.Criteria[1].Name);
        Assert.Equal(36, evaluation.Criteria[1].Score);
        Assert.Single(evaluation.Strengths);
        Assert.Single(evaluation.Improvements);
        Assert.Equal("ECON-101", evaluation.CourseId);
    }

    [Fact]
    public async Task Grade_SendsTheRubricAndTheAssessmentToTheModel()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var seeded = TestHelpers.SeedGradableSubmission(uow, userId);
        var ai = FakeAiService.AnsweringWith(GoodAnswer);
        var service = TestHelpers.CreateGradingService(uow, ai);

        await service.GradeAsync(seeded.SubmissionId, new GradingRequestDto(), userId);

        // A system message that fixes the answer, then the work itself
        var messages = Assert.Single(ai.Requests);
        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal("user", messages[1].Role);

        var prompt = messages[1].Content;
        Assert.Contains(seeded.Rubric.RubricContent, prompt);
        Assert.Contains("ECON-101", prompt);
        Assert.Contains("Ada Lovelace", prompt);
        Assert.Contains("S-1001", prompt);
        Assert.Contains("Rent", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Grade_StoresTheEvaluationOnTheSubmission()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var seeded = TestHelpers.SeedGradableSubmission(uow, userId);
        var service = TestHelpers.CreateGradingService(uow, FakeAiService.AnsweringWith(GoodAnswer));

        await service.GradeAsync(seeded.SubmissionId, new GradingRequestDto(), userId);

        var stored = Assert.Single(uow.SubmissionRows);
        Assert.Equal(GradeState.Complete, stored.GradingStatus);
        Assert.Equal(string.Empty, stored.FailureReason);
        Assert.False(string.IsNullOrWhiteSpace(stored.Evaluation));

        // The stored jsonb is the very evaluation that was handed back
        using var json = JsonDocument.Parse(stored.Evaluation!);
        Assert.Equal(88, json.RootElement.GetProperty("overallScore").GetDouble());
        Assert.Equal("B+", json.RootElement.GetProperty("grade").GetString());
    }

    [Fact]
    public async Task Grade_WorksOutThePercentageFromTheScoreAndTheMaximum()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var seeded = TestHelpers.SeedGradableSubmission(uow, userId);

        var service = TestHelpers.CreateGradingService(uow, FakeAiService.AnsweringWith(
            """{ "overallScore": 70, "maxScore": 80, "summary": "Solid." }"""));

        var result = await service.GradeAsync(seeded.SubmissionId, new GradingRequestDto(), userId);

        Assert.Equal(87.5, result.Evaluation!.Percentage);
    }

    [Fact]
    public async Task Grade_SumsTheCriterionMaximumsWhenTheModelStatesNoTotal()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var seeded = TestHelpers.SeedGradableSubmission(uow, userId);

        var service = TestHelpers.CreateGradingService(uow, FakeAiService.AnsweringWith(
            """
            {
              "overallScore": 76,
              "summary": "Passed.",
              "criteria": [
                { "name": "Thesis", "score": 36, "maxScore": 40 },
                { "name": "Evidence", "score": 40, "maxScore": 40 }
              ]
            }
            """));

        var result = await service.GradeAsync(seeded.SubmissionId, new GradingRequestDto(), userId);

        Assert.Equal(80, result.Evaluation!.MaxScore);
        Assert.Equal(95, result.Evaluation.Percentage);
    }

    [Fact]
    public async Task Grade_ReadsAnAnswerThatIsWrappedInAMarkdownFence()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var seeded = TestHelpers.SeedGradableSubmission(uow, userId);

        var service = TestHelpers.CreateGradingService(uow, FakeAiService.AnsweringWith(
            "Here you go:\n```json\n" + GoodAnswer + "\n```"));

        var result = await service.GradeAsync(seeded.SubmissionId, new GradingRequestDto(), userId);

        Assert.True(result.Succeeded);
        Assert.Equal(88, result.Evaluation!.OverallScore);
    }

    [Fact]
    public async Task Grade_PassesTheMarkerInstructionsOnToTheModel()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var seeded = TestHelpers.SeedGradableSubmission(uow, userId);
        var ai = FakeAiService.AnsweringWith(GoodAnswer);
        var service = TestHelpers.CreateGradingService(uow, ai);

        await service.GradeAsync(
            seeded.SubmissionId,
            new GradingRequestDto { Instructions = "weigh the sources heavily" },
            userId);

        Assert.Contains("weigh the sources heavily", ai.Requests[0][1].Content);
    }

    [Fact]
    public async Task Grade_MarksTheSubmissionFailedWhenTheProviderFails()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var seeded = TestHelpers.SeedGradableSubmission(uow, userId);

        var service = TestHelpers.CreateGradingService(uow, FakeAiService.Failing("Rate limit reached.", 429));

        var result = await service.GradeAsync(seeded.SubmissionId, new GradingRequestDto(), userId);

        Assert.False(result.Succeeded);
        Assert.Equal(429, result.StatusCode);

        var stored = Assert.Single(uow.SubmissionRows);
        Assert.Equal(GradeState.Failure, stored.GradingStatus);
        Assert.Equal("Rate limit reached.", stored.FailureReason);
        Assert.True(string.IsNullOrWhiteSpace(stored.Evaluation));
    }

    [Fact]
    public async Task Grade_MarksTheSubmissionFailedWhenTheAnswerCannotBeRead()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var seeded = TestHelpers.SeedGradableSubmission(uow, userId);

        var service = TestHelpers.CreateGradingService(uow, FakeAiService.AnsweringWith("I would rather not."));

        var result = await service.GradeAsync(seeded.SubmissionId, new GradingRequestDto(), userId);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
        Assert.Equal(GradeState.Failure, Assert.Single(uow.SubmissionRows).GradingStatus);
    }

    [Fact]
    public async Task Grade_RefusesASubmissionOwnedBySomebodyElse()
    {
        var uow = new InMemoryUnitOfWork();
        var seeded = TestHelpers.SeedGradableSubmission(uow, Guid.NewGuid());
        var ai = FakeAiService.AnsweringWith(GoodAnswer);
        var service = TestHelpers.CreateGradingService(uow, ai);

        var result = await service.GradeAsync(seeded.SubmissionId, new GradingRequestDto(), Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);

        // Nothing was graded and the provider was never asked
        Assert.Empty(ai.Requests);
        Assert.Equal(GradeState.NotStarted, Assert.Single(uow.SubmissionRows).GradingStatus);
    }

    [Fact]
    public async Task Grade_RefusesASubmissionWithoutARubric()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var seeded = TestHelpers.SeedGradableSubmission(uow, userId);
        var ai = FakeAiService.AnsweringWith(GoodAnswer);
        var service = TestHelpers.CreateGradingService(uow, ai);

        // Filed without a rubric, so there is no guide to grade by
        uow.Submissions.Update(seeded.Submission);
        seeded.Submission.RubricId = null;

        var result = await service.GradeAsync(seeded.SubmissionId, new GradingRequestDto(), userId);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, result.StatusCode);
        Assert.Contains("not filed against a rubric", result.ErrorMessage);
        Assert.Empty(ai.Requests);
    }

    [Fact]
    public async Task Grade_RefusesASubmissionWithoutAnUploadedDocument()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var seeded = TestHelpers.SeedGradableSubmission(uow, userId);
        var ai = FakeAiService.AnsweringWith(GoodAnswer);
        var service = TestHelpers.CreateGradingService(uow, ai);

        uow.Submissions.Update(seeded.Submission);
        seeded.Submission.DocumentId = null;

        var result = await service.GradeAsync(seeded.SubmissionId, new GradingRequestDto(), userId);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, result.StatusCode);
        Assert.Contains("no assessment to grade", result.ErrorMessage);
        Assert.Empty(ai.Requests);
    }

    [Fact]
    public async Task Grade_RefusesADocumentNothingCouldBeReadOutOf()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();

        // An empty assessment is not something to grade
        var seeded = TestHelpers.SeedGradableSubmission(uow, userId, assessmentText: "   ");
        var service = TestHelpers.CreateGradingService(uow, FakeAiService.AnsweringWith(GoodAnswer));

        var result = await service.GradeAsync(seeded.SubmissionId, new GradingRequestDto(), userId);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, result.StatusCode);
    }

    [Fact]
    public async Task Grade_RefusesAnAssessmentThatIsTooShortToGrade()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();

        var seeded = TestHelpers.SeedGradableSubmission(uow, userId, assessmentText: "It was fine.");
        var service = TestHelpers.CreateGradingService(uow, FakeAiService.AnsweringWith(GoodAnswer));

        var result = await service.GradeAsync(seeded.SubmissionId, new GradingRequestDto(), userId);

        Assert.False(result.Succeeded);
        Assert.Contains("too little text", result.ErrorMessage);
    }

    [Fact]
    public async Task Grade_CutsAnAssessmentThatWouldNotFitIntoOneRequest()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();

        // Comfortably over the 100 000 character limit of the Ai section
        var essay = string.Concat(Enumerable.Repeat("inflation squeezes the household budget. ", 4_000));
        var seeded = TestHelpers.SeedGradableSubmission(uow, userId, assessmentText: essay);

        var ai = FakeAiService.AnsweringWith(GoodAnswer);
        var service = TestHelpers.CreateGradingService(uow, ai);

        var result = await service.GradeAsync(seeded.SubmissionId, new GradingRequestDto(), userId);

        // It is still graded, the prompt is just cut short and says so
        Assert.True(result.Succeeded);
        Assert.True(result.Evaluation!.AssessmentTruncated);
        Assert.Contains("the assessment was cut short", ai.Requests[0][1].Content);
    }
}
