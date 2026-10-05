using GradrTab.Controllers;
using GradrTab.DTOs;
using GradrTab.Tests.Support;
using Microsoft.AspNetCore.Mvc;

namespace GradrTab.Tests;

public class SubmissionsControllerTests
{
    private static SubmissionsController CreateController(
        InMemoryUnitOfWork uow,
        Guid? userId = null,
        FakeAiService? aiService = null) =>
        TestHelpers.CreateSubmissionsController(uow, aiService, userId);

    [Fact]
    public async Task Create_ReturnsCreatedAtTheNewSubmission()
    {
        var uow = new InMemoryUnitOfWork();
        var controller = CreateController(uow, Guid.NewGuid());

        var result = await controller.Create(TestHelpers.SampleSubmission(), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(SubmissionsController.GetById), created.ActionName);

        var submission = Assert.IsType<SubmissionResponseDto>(created.Value);
        Assert.Equal("S-1001", submission.StudentId);
        Assert.Equal("NotStarted", submission.GradingStatus);
        Assert.Single(uow.SubmissionRows);
    }

    [Fact]
    public async Task Create_WithoutAUserIdAnswersUnauthorized()
    {
        var uow = new InMemoryUnitOfWork();
        var controller = CreateController(uow);

        var result = await controller.Create(TestHelpers.SampleSubmission(), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Result);
        Assert.Empty(uow.SubmissionRows);
    }

    [Fact]
    public async Task GetById_ReturnsTheStoredSubmission()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();

        var created = await CreateController(uow, userId).Create(TestHelpers.SampleSubmission(), CancellationToken.None);
        var submission = Assert.IsType<SubmissionResponseDto>(
            Assert.IsType<CreatedAtActionResult>(created.Result).Value);

        var result = await CreateController(uow, userId).GetById(submission.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(submission.Id, Assert.IsType<SubmissionResponseDto>(ok.Value).Id);
    }

    [Fact]
    public async Task GetById_AnswersNotFoundForAMissingSubmission()
    {
        var uow = new InMemoryUnitOfWork();
        var controller = CreateController(uow, Guid.NewGuid());

        var result = await controller.GetById(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task List_OnlyReturnsTheSubmissionsOfTheCaller()
    {
        var uow = new InMemoryUnitOfWork();
        var mine = Guid.NewGuid();

        await CreateController(uow, mine).Create(TestHelpers.SampleSubmission(), CancellationToken.None);
        await CreateController(uow, Guid.NewGuid()).Create(TestHelpers.SampleSubmission(), CancellationToken.None);

        var result = await CreateController(uow, mine)
            .List(rubricId: null, page: 1, pageSize: 20, CancellationToken.None);

        var page = Assert.IsType<PagedResultDto<SubmissionSummaryDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(1, page.TotalCount);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task List_CanBeNarrowedDownToOneRubric()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var rubricId = Guid.NewGuid();

        await CreateController(uow, userId).Create(TestHelpers.SampleSubmission(rubricId), CancellationToken.None);
        await CreateController(uow, userId).Create(TestHelpers.SampleSubmission(Guid.NewGuid()), CancellationToken.None);

        var result = await CreateController(uow, userId)
            .List(rubricId: rubricId, page: 1, pageSize: 20, CancellationToken.None);

        var page = Assert.IsType<PagedResultDto<SubmissionSummaryDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Update_ReturnsTheUpdatedSubmission()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();

        var created = await CreateController(uow, userId).Create(TestHelpers.SampleSubmission(), CancellationToken.None);
        var submission = Assert.IsType<SubmissionResponseDto>(
            Assert.IsType<CreatedAtActionResult>(created.Result).Value);

        var edit = TestHelpers.SampleSubmission();
        edit.StudentName = "Ada Byron";

        var result = await CreateController(uow, userId)
            .Update(submission.Id, edit, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Ada Byron", Assert.IsType<SubmissionResponseDto>(ok.Value).StudentName);
    }

    [Fact]
    public async Task Update_AnswersNotFoundForAMissingSubmission()
    {
        var uow = new InMemoryUnitOfWork();
        var controller = CreateController(uow, Guid.NewGuid());

        var result = await controller.Update(Guid.NewGuid(), TestHelpers.SampleSubmission(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();

        var created = await CreateController(uow, userId).Create(TestHelpers.SampleSubmission(), CancellationToken.None);
        var submission = Assert.IsType<SubmissionResponseDto>(
            Assert.IsType<CreatedAtActionResult>(created.Result).Value);

        var result = await CreateController(uow, userId).Delete(submission.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(uow.SubmissionRows);
    }

    [Fact]
    public async Task Delete_AnswersNotFoundForASubmissionOfSomebodyElse()
    {
        var uow = new InMemoryUnitOfWork();

        var created = await CreateController(uow, Guid.NewGuid())
            .Create(TestHelpers.SampleSubmission(), CancellationToken.None);
        var submission = Assert.IsType<SubmissionResponseDto>(
            Assert.IsType<CreatedAtActionResult>(created.Result).Value);

        var result = await CreateController(uow, Guid.NewGuid()).Delete(submission.Id, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Single(uow.SubmissionRows);
    }

    [Fact]
    public async Task Grade_ReturnsTheEvaluation()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var seeded = TestHelpers.SeedGradableSubmission(uow, userId);

        var controller = CreateController(
            uow,
            userId,
            FakeAiService.AnsweringWith(
                """{ "overallScore": 88, "maxScore": 100, "grade": "B+", "summary": "Solid work." }"""));

        var result = await controller.Grade(seeded.SubmissionId, null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var evaluation = Assert.IsType<EvaluationDto>(ok.Value);

        Assert.Equal(seeded.SubmissionId, evaluation.SubmissionId);
        Assert.Equal("B+", evaluation.Grade);
        Assert.Equal(GradeState.Complete, Assert.Single(uow.SubmissionRows).GradingStatus);
    }

    [Fact]
    public async Task Grade_WithoutAUserIdAnswersUnauthorized()
    {
        var uow = new InMemoryUnitOfWork();
        var seeded = TestHelpers.SeedGradableSubmission(uow, Guid.NewGuid());

        var controller = CreateController(uow);

        var result = await controller.Grade(seeded.SubmissionId, null, CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task Grade_AnswersNotFoundForAMissingSubmission()
    {
        var uow = new InMemoryUnitOfWork();
        var controller = CreateController(uow, Guid.NewGuid());

        var result = await controller.Grade(Guid.NewGuid(), new GradingRequestDto(), CancellationToken.None);

        Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(404, ((ObjectResult)result.Result).StatusCode);
    }

    [Fact]
    public async Task Grade_AnswersUnprocessableWhenThereIsNothingToGrade()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var seeded = TestHelpers.SeedGradableSubmission(uow, userId);
        seeded.Submission.DocumentId = null;

        var controller = CreateController(uow, userId);

        var result = await controller.Grade(seeded.SubmissionId, null, CancellationToken.None);

        var unprocessable = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(422, unprocessable.StatusCode);
    }
}
