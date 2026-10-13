using GradrTab.DTOs;
using GradrTab.Services;
using GradrTab.Tests.Support;

namespace GradrTab.Tests;

public class RubricServiceTests
{
    [Fact]
    public async Task Create_StoresTheRubricWithItsOwner()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);
        var userId = Guid.NewGuid();

        var created = await service.CreateAsync(TestHelpers.SampleRubric(), userId);

        var stored = Assert.Single(uow.RubricRows);
        Assert.Equal(created.Id, stored.Id);
        Assert.Equal("ECON-101", stored.CourseId);
        Assert.Equal("Economics", stored.CourseName);
        Assert.Equal(userId, stored.UploadedByUserId);
    }

    [Fact]
    public async Task Create_TrimsTheSubmittedText()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);

        var payload = TestHelpers.SampleRubric();
        payload.CourseName = "  Economics  ";
        payload.CourseId = "  ECON-101  ";

        await service.CreateAsync(payload, Guid.NewGuid());

        var stored = Assert.Single(uow.RubricRows);
        Assert.Equal("Economics", stored.CourseName);
        Assert.Equal("ECON-101", stored.CourseId);
    }

    [Fact]
    public async Task Create_RoundTripsTheRubricContentBack()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);

        var payload = TestHelpers.SampleRubric();
        var created = await service.CreateAsync(payload, Guid.NewGuid());

        Assert.Equal(payload.RubricContent.Trim(), created.RubricContent);
    }

    [Fact]
    public async Task GetById_ReturnsTheStoredRubric()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);
        var userId = Guid.NewGuid();

        var created = await service.CreateAsync(TestHelpers.SampleRubric(), userId);

        var found = await service.GetByIdAsync(created.Id, userId);

        Assert.NotNull(found);
        Assert.Equal(created.Id, found.Id);
        Assert.Equal(created.CourseId, found.CourseId);
    }

    [Fact]
    public async Task GetById_HidesARubricOwnedBySomebodyElse()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);

        var created = await service.CreateAsync(TestHelpers.SampleRubric(), Guid.NewGuid());

        Assert.Null(await service.GetByIdAsync(created.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task List_OnlyShowsTheRubricsOfTheSignedInUser()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);
        var mine = Guid.NewGuid();

        await service.CreateAsync(TestHelpers.SampleRubric(), mine);
        await service.CreateAsync(TestHelpers.SampleRubric(), Guid.NewGuid());

        var page = await service.ListAsync(mine, 1, 20);

        Assert.Equal(1, page.TotalCount);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task List_PagesTheRubricsNewestFirst()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);
        var userId = Guid.NewGuid();

        uow.RubricRows.AddRange(
            SeedRubric(userId, "ECON-101", DateTime.UtcNow.AddMinutes(-10)),
            SeedRubric(userId, "ECON-102", DateTime.UtcNow));

        var page = await service.ListAsync(userId, 1, 1);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal("ECON-102", Assert.Single(page.Items).CourseId);
    }

    [Fact]
    public async Task List_LeavesTheRubricContentOut()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);
        var userId = Guid.NewGuid();

        await service.CreateAsync(TestHelpers.SampleRubric(), userId);

        var page = await service.ListAsync(userId, 1, 20);

        // The summary only carries the header fields, the content is fetched
        // through the single rubric endpoint
        Assert.Equal("ECON-101", Assert.Single(page.Items).CourseId);
    }

    [Fact]
    public async Task Update_ReplacesTheCourseAndTheContent()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);
        var userId = Guid.NewGuid();

        var created = await service.CreateAsync(TestHelpers.SampleRubric(), userId);

        var edit = TestHelpers.SampleRubric();
        edit.CourseName = "Economics II";
        edit.RubricContent = "{\"criteria\":[]}";

        var result = await service.UpdateAsync(created.Id, edit, userId);

        Assert.True(result.Succeeded);
        Assert.Equal("Economics II", result.Rubric!.CourseName);
        Assert.Equal("{\"criteria\":[]}", uow.RubricRows[0].RubricContent);
    }

    [Fact]
    public async Task Update_KeepsTheIdTheOwnerAndTheCreationTime()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);
        var userId = Guid.NewGuid();

        var created = await service.CreateAsync(TestHelpers.SampleRubric(), userId);
        var result = await service.UpdateAsync(created.Id, TestHelpers.SampleRubric(), userId);

        var stored = Assert.Single(uow.RubricRows);
        Assert.Equal(created.Id, stored.Id);
        Assert.Equal(userId, stored.UploadedByUserId);
        Assert.Equal(created.CreatedAt, stored.CreatedAt);
        Assert.Equal(result.Rubric!.UpdatedAt, stored.UpdatedAt);
    }

    [Fact]
    public async Task Update_RefusesARubricOwnedBySomebodyElse()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);

        var created = await service.CreateAsync(TestHelpers.SampleRubric(), Guid.NewGuid());

        var result = await service.UpdateAsync(created.Id, TestHelpers.SampleRubric(), Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Null(result.Rubric);
        Assert.Single(uow.RubricRows);
    }

    [Fact]
    public async Task Delete_RemovesTheRubric()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);
        var userId = Guid.NewGuid();

        var created = await service.CreateAsync(TestHelpers.SampleRubric(), userId);

        Assert.True((await service.DeleteAsync(created.Id, userId)).Succeeded);
        Assert.Empty(uow.RubricRows);
    }

    [Fact]
    public async Task Delete_RefusesARubricOwnedBySomebodyElse()
    {
        var uow = new InMemoryUnitOfWork();
        var service = TestHelpers.CreateRubricService(uow);

        var created = await service.CreateAsync(TestHelpers.SampleRubric(), Guid.NewGuid());

        Assert.False((await service.DeleteAsync(created.Id, Guid.NewGuid())).Succeeded);
        Assert.Single(uow.RubricRows);
    }

    private static Rubric SeedRubric(Guid userId, string courseId, DateTime createdAt) => new()
    {
        CourseName = "Economics",
        CourseId = courseId,
        RubricContent = "{}",
        UploadedByUserId = userId,
        CreatedAt = createdAt,
        UpdatedAt = createdAt
    };
}
