using GradrTab.DTOs;
using GradrTab.Services;
using GradrTab.Tests.Support;

namespace GradrTab.Tests;

public class SubmissionServiceTests
{
    [Fact]
    public async Task Create_StoresTheSubmissionAsNotStarted()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateSubmissionService(uow);
        var userId = Guid.NewGuid();

        var created = await service.CreateAsync(TestHelpers.SampleSubmission(), userId);

        var stored = Assert.Single(uow.SubmissionRows);
        Assert.Equal(created.Id, stored.Id);
        Assert.Equal(userId, stored.UploadedByUserId);
        Assert.Equal(GradeState.NotStarted, stored.GradingStatus);
        Assert.Equal("NotStarted", created.GradingStatus);
    }

    [Fact]
    public async Task Create_KeepsTheRubricAndDocumentReferences()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateSubmissionService(uow);

        var rubricId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        await service.CreateAsync(TestHelpers.SampleSubmission(rubricId, documentId), Guid.NewGuid());

        var stored = Assert.Single(uow.SubmissionRows);
        Assert.Equal(rubricId, stored.RubricId);
        Assert.Equal(documentId, stored.DocumentId);
    }

    [Fact]
    public async Task Create_TrimsTheStudentDetails()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateSubmissionService(uow);

        var payload = TestHelpers.SampleSubmission();
        payload.StudentId = "  S-1001  ";
        payload.StudentName = "  Ada Lovelace  ";

        await service.CreateAsync(payload, Guid.NewGuid());

        var stored = Assert.Single(uow.SubmissionRows);
        Assert.Equal("S-1001", stored.StudentId);
        Assert.Equal("Ada Lovelace", stored.StudentName);
    }

    [Fact]
    public async Task GetById_ReturnsTheStoredSubmission()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateSubmissionService(uow);
        var userId = Guid.NewGuid();

        var created = await service.CreateAsync(TestHelpers.SampleSubmission(), userId);

        var found = await service.GetByIdAsync(created.Id, userId);

        Assert.NotNull(found);
        Assert.Equal("S-1001", found.StudentId);
    }

    [Fact]
    public async Task GetById_HidesASubmissionOwnedBySomebodyElse()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateSubmissionService(uow);

        var created = await service.CreateAsync(TestHelpers.SampleSubmission(), Guid.NewGuid());

        Assert.Null(await service.GetByIdAsync(created.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task List_OnlyShowsTheSubmissionsOfTheSignedInUser()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateSubmissionService(uow);
        var mine = Guid.NewGuid();

        await service.CreateAsync(TestHelpers.SampleSubmission(), mine);
        await service.CreateAsync(TestHelpers.SampleSubmission(), Guid.NewGuid());

        var page = await service.ListAsync(mine, null, 1, 20);

        Assert.Equal(1, page.TotalCount);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task List_CanBeNarrowedDownToOneRubric()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateSubmissionService(uow);
        var userId = Guid.NewGuid();

        var wanted = Guid.NewGuid();
        var other = Guid.NewGuid();

        await service.CreateAsync(TestHelpers.SampleSubmission(wanted), userId);
        await service.CreateAsync(TestHelpers.SampleSubmission(other), userId);

        var page = await service.ListAsync(userId, wanted, 1, 20);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(wanted, Assert.Single(page.Items).RubricId);
    }

    [Fact]
    public async Task List_ListsEverySubmissionWhenNoRubricIsGiven()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateSubmissionService(uow);
        var userId = Guid.NewGuid();

        await service.CreateAsync(TestHelpers.SampleSubmission(Guid.NewGuid()), userId);
        await service.CreateAsync(TestHelpers.SampleSubmission(null), userId);

        var page = await service.ListAsync(userId, null, 1, 20);

        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task Update_ReplacesTheStudentTheRubricAndTheEvaluation()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateSubmissionService(uow);
        var userId = Guid.NewGuid();

        var created = await service.CreateAsync(TestHelpers.SampleSubmission(), userId);

        var rubricId = Guid.NewGuid();
        var edit = TestHelpers.SampleSubmission(rubricId);
        edit.StudentName = "Ada Byron";
        edit.Evaluation = "{\"score\":88}";

        var result = await service.UpdateAsync(created.Id, edit, userId);

        Assert.True(result.Succeeded);

        var stored = Assert.Single(uow.SubmissionRows);
        Assert.Equal("Ada Byron", stored.StudentName);
        Assert.Equal(rubricId, stored.RubricId);
        Assert.Equal("{\"score\":88}", stored.Evaluation);
    }

    [Fact]
    public async Task Update_LeavesTheGradingStateAndTheCreationTimeAlone()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateSubmissionService(uow);
        var userId = Guid.NewGuid();

        var created = await service.CreateAsync(TestHelpers.SampleSubmission(), userId);

        var row = Assert.Single(uow.SubmissionRows);
        row.GradingStatus = GradeState.InProgress;
        row.FailureReason = string.Empty;

        await service.UpdateAsync(created.Id, TestHelpers.SampleSubmission(), userId);

        // Editing the submission does not touch where it sits in the pipeline
        Assert.Equal(GradeState.InProgress, row.GradingStatus);
        Assert.Equal(created.CreatedAt, row.CreatedAt);
        Assert.Equal(userId, row.UploadedByUserId);
    }

    [Fact]
    public async Task Update_RefusesASubmissionOwnedBySomebodyElse()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateSubmissionService(uow);

        var created = await service.CreateAsync(TestHelpers.SampleSubmission(), Guid.NewGuid());

        var result = await service.UpdateAsync(created.Id, TestHelpers.SampleSubmission(), Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Null(result.Submission);
        Assert.Single(uow.SubmissionRows);
    }

    [Fact]
    public async Task Delete_RemovesTheSubmission()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateSubmissionService(uow);
        var userId = Guid.NewGuid();

        var created = await service.CreateAsync(TestHelpers.SampleSubmission(), userId);

        Assert.True((await service.DeleteAsync(created.Id, userId)).Succeeded);
        Assert.Empty(uow.SubmissionRows);
    }

    [Fact]
    public async Task Delete_RefusesASubmissionOwnedBySomebodyElse()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateSubmissionService(uow);

        var created = await service.CreateAsync(TestHelpers.SampleSubmission(), Guid.NewGuid());

        Assert.False((await service.DeleteAsync(created.Id, Guid.NewGuid())).Succeeded);
        Assert.Single(uow.SubmissionRows);
    }

    [Fact]
    public async Task GetById_HandsBackTheStoredEvaluationAsAnObject()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateSubmissionService(uow);
        var userId = Guid.NewGuid();

        var created = await service.CreateAsync(TestHelpers.SampleSubmission(), userId);

        // What the grading service writes into the jsonb column
        var row = Assert.Single(uow.SubmissionRows);
        row.Evaluation = """{ "overallScore": 88, "maxScore": 100, "grade": "B+" }""";
        row.GradingStatus = GradeState.Complete;

        var found = await service.GetByIdAsync(created.Id, userId);

        Assert.NotNull(found!.Evaluation);
        Assert.Equal(88, found.Evaluation.OverallScore);
        Assert.Equal("B+", found.Evaluation.Grade);
    }

    [Fact]
    public async Task GetById_ReportsNoEvaluationForAnUngradedSubmission()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateSubmissionService(uow);
        var userId = Guid.NewGuid();

        var created = await service.CreateAsync(TestHelpers.SampleSubmission(), userId);

        var found = await service.GetByIdAsync(created.Id, userId);

        Assert.Null(found!.Evaluation);
        Assert.Equal("NotStarted", found.GradingStatus);
    }
}
