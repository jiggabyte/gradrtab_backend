using System.Security.Claims;
using GradrTab.Controllers;
using GradrTab.DTOs;
using GradrTab.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace GradrTab.Tests;

public class RubricsControllerTests
{
    private static RubricsController CreateController(InMemoryUnitOfWork uow, Guid? userId = null)
    {
        var controller = new RubricsController(
            TestHelpers.CreateRubricService(uow),
            NullLogger<RubricsController>.Instance);

        var claims = new List<Claim>();
        if (userId is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
        }

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
            }
        };

        return controller;
    }

    [Fact]
    public async Task Create_ReturnsCreatedAtTheNewRubric()
    {
        var uow = new InMemoryUnitOfWork();
        var controller = CreateController(uow, Guid.NewGuid());

        var result = await controller.Create(TestHelpers.SampleRubric(), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var rubric = Assert.IsType<RubricResponseDto>(created.Value);

        Assert.Equal("ECON-101", rubric.CourseId);
        Assert.Equal("Economics", rubric.CourseName);
        Assert.Single(uow.RubricRows);
    }

    [Fact]
    public async Task Create_PointsAtTheGetByIdRoute()
    {
        var uow = new InMemoryUnitOfWork();
        var controller = CreateController(uow, Guid.NewGuid());

        var result = await controller.Create(TestHelpers.SampleRubric(), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(RubricsController.GetById), created.ActionName);
    }

    [Fact]
    public async Task Create_WithoutAUserIdAnswersUnauthorized()
    {
        var uow = new InMemoryUnitOfWork();
        var controller = CreateController(uow);

        var result = await controller.Create(TestHelpers.SampleRubric(), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Result);
        Assert.Empty(uow.RubricRows);
    }

    [Fact]
    public async Task GetById_ReturnsTheStoredRubric()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var controller = CreateController(uow, userId);

        var created = await controller.Create(TestHelpers.SampleRubric(), CancellationToken.None);
        var rubric = Assert.IsType<RubricResponseDto>(
            Assert.IsType<CreatedAtActionResult>(created.Result).Value);

        var result = await controller.GetById(rubric.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(rubric.Id, Assert.IsType<RubricResponseDto>(ok.Value).Id);
    }

    [Fact]
    public async Task GetById_AnswersNotFoundForAMissingRubric()
    {
        var uow = new InMemoryUnitOfWork();
        var controller = CreateController(uow, Guid.NewGuid());

        var result = await controller.GetById(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetById_AnswersNotFoundForARubricOfSomebodyElse()
    {
        var uow = new InMemoryUnitOfWork();

        var created = await CreateController(uow, Guid.NewGuid())
            .Create(TestHelpers.SampleRubric(), CancellationToken.None);
        var rubric = Assert.IsType<RubricResponseDto>(
            Assert.IsType<CreatedAtActionResult>(created.Result).Value);

        var result = await CreateController(uow, Guid.NewGuid()).GetById(rubric.Id, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task List_OnlyReturnsTheRubricsOfTheCaller()
    {
        var uow = new InMemoryUnitOfWork();
        var mine = Guid.NewGuid();

        await CreateController(uow, mine).Create(TestHelpers.SampleRubric(), CancellationToken.None);
        await CreateController(uow, Guid.NewGuid()).Create(TestHelpers.SampleRubric(), CancellationToken.None);

        var result = await CreateController(uow, mine).List(page: 1, pageSize: 20, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<PagedResultDto<RubricSummaryDto>>(ok.Value);

        Assert.Equal(1, page.TotalCount);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task List_PassesThePagingOn()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();

        await CreateController(uow, userId).Create(TestHelpers.SampleRubric(), CancellationToken.None);
        await CreateController(uow, userId).Create(TestHelpers.SampleRubric(), CancellationToken.None);

        var result = await CreateController(uow, userId).List(page: 2, pageSize: 1, CancellationToken.None);

        var page = Assert.IsType<PagedResultDto<RubricSummaryDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task Update_ReturnsTheUpdatedRubric()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();

        var created = await CreateController(uow, userId).Create(TestHelpers.SampleRubric(), CancellationToken.None);
        var rubric = Assert.IsType<RubricResponseDto>(
            Assert.IsType<CreatedAtActionResult>(created.Result).Value);

        var edit = TestHelpers.SampleRubric();
        edit.CourseName = "Economics II";

        var result = await CreateController(uow, userId).Update(rubric.Id, edit, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Economics II", Assert.IsType<RubricResponseDto>(ok.Value).CourseName);
    }

    [Fact]
    public async Task Update_AnswersNotFoundForAMissingRubric()
    {
        var uow = new InMemoryUnitOfWork();
        var controller = CreateController(uow, Guid.NewGuid());

        var result = await controller.Update(Guid.NewGuid(), TestHelpers.SampleRubric(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();

        var created = await CreateController(uow, userId).Create(TestHelpers.SampleRubric(), CancellationToken.None);
        var rubric = Assert.IsType<RubricResponseDto>(
            Assert.IsType<CreatedAtActionResult>(created.Result).Value);

        var result = await CreateController(uow, userId).Delete(rubric.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(uow.RubricRows);
    }

    [Fact]
    public async Task Delete_AnswersNotFoundForARubricOfSomebodyElse()
    {
        var uow = new InMemoryUnitOfWork();

        var created = await CreateController(uow, Guid.NewGuid())
            .Create(TestHelpers.SampleRubric(), CancellationToken.None);
        var rubric = Assert.IsType<RubricResponseDto>(
            Assert.IsType<CreatedAtActionResult>(created.Result).Value);

        var result = await CreateController(uow, Guid.NewGuid()).Delete(rubric.Id, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Single(uow.RubricRows);
    }
}
