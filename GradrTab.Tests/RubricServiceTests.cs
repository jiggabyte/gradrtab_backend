using GradrTab.DTOs;
using GradrTab.Services;
using GradrTab.Tests.Support;

namespace GradrTab.Tests;

public class RubricServiceTests
{
    [Fact]
    public async Task Submit_StoresTheCriteriaAndLevelsInOrder()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);

        var result = await service.SubmitAsync(TestHelpers.SampleRubric(), Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);

        var stored = Assert.Single(uow.RubricRows);
        Assert.Equal("rub_essay", stored.RubricKey);
        Assert.Equal("Research Essay", stored.Title);
        Assert.Equal(3, stored.CriterionCount);

        // The stored rows keep the order the frontend sent
        var ordered = stored.Criteria.OrderBy(c => c.Order).ToList();

        Assert.Equal(["thesis", "evidence", "mechanics"], ordered.Select(c => c.CriterionKey));
        Assert.Equal(["Strong", "Adequate", "Weak"], ordered[0].Levels.OrderBy(l => l.Order).Select(l => l.Label));
    }

    [Fact]
    public async Task Submit_DerivesTheMaxPointsFromTheTopLevel()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);

        var result = await service.SubmitAsync(TestHelpers.SampleRubric(), null);

        // 40 thesis + 40 evidence + 20 mechanics
        Assert.Equal(100, result.Rubric!.MaxPoints);
        Assert.Equal(40, result.Rubric.Criteria[0].MaxPoints);
        Assert.Equal(20, result.Rubric.Criteria[2].MaxPoints);
    }

    [Fact]
    public async Task Submit_RoundTripsTheSameShapeBack()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);

        var submitted = TestHelpers.SampleRubric();
        var result = await service.SubmitAsync(submitted, null);

        var returned = result.Rubric!;

        Assert.Equal(submitted.Id, returned.RubricKey);
        Assert.Equal(submitted.Title, returned.Title);
        Assert.Equal(3, returned.Criteria.Count);

        // Labels, points and descriptions all survive
        var firstLevel = returned.Criteria[0].Levels[0];
        Assert.Equal("Strong", firstLevel.Label);
        Assert.Equal(40, firstLevel.Points);
        Assert.Equal("Clear, arguable thesis sustained throughout.", firstLevel.Description);
    }

    [Fact]
    public async Task Submit_RejectsDuplicateCriterionIds()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);

        var payload = TestHelpers.SampleRubric();
        payload.Criteria[1].Id = "thesis";

        var result = await service.SubmitAsync(payload, null);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("reuses the id 'thesis'"));
        Assert.Empty(uow.RubricRows);
    }

    [Fact]
    public async Task Submit_RejectsLevelsThatAreNotInDescendingOrder()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);

        var payload = TestHelpers.SampleRubric();
        // Weak is worth more than Strong, which is the wrong way round
        payload.Criteria[0].Levels[0].Points = 5;
        payload.Criteria[0].Levels[2].Points = 40;

        var result = await service.SubmitAsync(payload, null);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("from the highest points to the lowest"));
    }

    [Fact]
    public async Task Submit_RejectsNonPositivePoints()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);

        var payload = TestHelpers.SampleRubric();
        payload.Criteria[0].Levels[1].Points = 0;

        var result = await service.SubmitAsync(payload, null);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("must be worth at least 1 point"));
    }

    [Fact]
    public async Task Submit_RejectsACriterionWithoutLevels()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);

        var payload = TestHelpers.SampleRubric();
        payload.Criteria[0].Levels = [];

        var result = await service.SubmitAsync(payload, null);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("has no levels"));
    }

    [Fact]
    public async Task Submit_ReportsEveryProblemAtOnce()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);

        var payload = TestHelpers.SampleRubric();
        payload.Criteria[0].Levels[0].Points = 0;
        payload.Criteria[1].Levels = [];

        var result = await service.SubmitAsync(payload, null);

        // Two separate problems, not just the first one found
        Assert.True(result.Errors.Count >= 2, $"expected several errors, got: {string.Join(" | ", result.Errors)}");
    }

    [Fact]
    public async Task Submit_RespectsTheConfiguredLimits()
    {
        var uow = new InMemoryUnitOfWork();

        var options = TestHelpers.DefaultRubricOptions();
        options.MaxLevelsPerCriterion = 2;

        var service = TestHelpers.CreateRubricService(uow, options);

        var result = await service.SubmitAsync(TestHelpers.SampleRubric(), null);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("at most 2 are allowed"));
    }

    [Fact]
    public async Task Submit_ReplacesTheRubricWhenTheSameKeyComesBack()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);
        var userId = Guid.NewGuid();

        var first = await service.SubmitAsync(TestHelpers.SampleRubric(), userId);

        var edited = TestHelpers.SampleRubric();
        edited.Title = "Research Essay v2";
        var second = await service.SubmitAsync(edited, userId);

        // Same rubric, updated in place rather than duplicated
        Assert.Equal(first.Rubric!.Id, second.Rubric!.Id);
        Assert.Single(uow.RubricRows);
        Assert.Equal("Research Essay v2", uow.RubricRows[0].Title);
    }

    [Fact]
    public async Task Submit_KeepsTheSameKeyApartForDifferentUsers()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);

        await service.SubmitAsync(TestHelpers.SampleRubric(), Guid.NewGuid());
        await service.SubmitAsync(TestHelpers.SampleRubric(), Guid.NewGuid());

        // Two users may both own a rubric called rub_essay
        Assert.Equal(2, uow.RubricRows.Count);
    }

    [Fact]
    public async Task Submit_KeepsTheStoredRubricWhenAResubmissionIsRejected()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);
        var userId = Guid.NewGuid();

        var first = await service.SubmitAsync(TestHelpers.SampleRubric(), userId);

        // A broken edit of an existing rubric must not cost the user the old one
        var broken = TestHelpers.SampleRubric();
        broken.Title = "Research Essay v2";
        broken.Criteria[0].Levels[0].Points = -5;

        var rejected = await service.SubmitAsync(broken, userId);

        Assert.False(rejected.Succeeded);

        var stored = Assert.Single(uow.RubricRows);
        Assert.Equal(first.Rubric!.Id, stored.Id);
        Assert.Equal("Research Essay", stored.Title);
    }

    [Fact]
    public async Task List_OnlyShowsTheRubricsOfTheSignedInUser()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);

        var mine = Guid.NewGuid();
        await service.SubmitAsync(TestHelpers.SampleRubric(), mine);
        await service.SubmitAsync(TestHelpers.SampleRubric(), Guid.NewGuid());

        var page = await service.ListAsync(mine, 1, 20);

        Assert.Equal(1, page.TotalCount);
        Assert.Single(page.Items);
        Assert.Equal("rub_essay", page.Items[0].RubricKey);
    }

    [Fact]
    public async Task Delete_RemovesTheRubric()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);

        var submitted = await service.SubmitAsync(TestHelpers.SampleRubric(), null);

        Assert.True(await service.DeleteAsync(submitted.Rubric!.Id));
        Assert.Empty(uow.RubricRows);
    }

    [Fact]
    public async Task Delete_RefusesARubricOwnedBySomebodyElse()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);

        var owner = Guid.NewGuid();
        var submitted = await service.SubmitAsync(TestHelpers.SampleRubric(), owner);

        Assert.False(await service.DeleteAsync(submitted.Rubric!.Id, Guid.NewGuid()));
        Assert.Single(uow.RubricRows);
    }
}
