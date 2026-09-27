using System.Security.Claims;
using System.Text.Json;
using GradrTab.Controllers;
using GradrTab.DTOs;
using GradrTab.Services;
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
    public async Task Submit_StoresTheRubricFromThePayload()
    {
        var uow = new InMemoryUnitOfWork();
        var controller = CreateController(uow, Guid.NewGuid());

        var result = await controller.Submit(TestHelpers.SampleRubric(), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var rubric = Assert.IsType<RubricResponseDto>(created.Value);

        Assert.Equal("rub_essay", rubric.RubricKey);
        Assert.Equal("Research Essay", rubric.Title);
        Assert.Equal(100, rubric.MaxPoints);
        Assert.Equal(3, rubric.CriterionCount);
        Assert.Single(uow.RubricRows);
    }

    [Fact]
    public async Task Submit_ReturnsEveryValidationProblem()
    {
        var uow = new InMemoryUnitOfWork();
        var controller = CreateController(uow, Guid.NewGuid());

        var payload = TestHelpers.SampleRubric();
        payload.Criteria[1].Id = "thesis";

        var result = await controller.Submit(payload, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);

        // The anonymous envelope has to be serialized to see the messages inside
        var body = JsonSerializer.Serialize(badRequest.Value);

        Assert.Contains("errors", body);
        Assert.Contains("reuses the id", body);
        Assert.Empty(uow.RubricRows);
    }

    [Fact]
    public async Task GetByKey_ReturnsTheStoredRubric()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var controller = CreateController(uow, userId);

        await controller.Submit(TestHelpers.SampleRubric(), CancellationToken.None);
        var result = await controller.GetByKey("rub_essay", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var rubric = Assert.IsType<RubricResponseDto>(ok.Value);
        Assert.Equal(3, rubric.Criteria.Count);
    }

    [Fact]
    public async Task GetByKey_ReturnsNotFoundForAMissingKey()
    {
        var uow = new InMemoryUnitOfWork();
        var controller = CreateController(uow, Guid.NewGuid());

        var result = await controller.GetByKey("nope", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task List_OnlyReturnsTheRubricsOfTheCaller()
    {
        var uow = new InMemoryUnitOfWork();
        var mine = Guid.NewGuid();

        await CreateController(uow, mine).Submit(TestHelpers.SampleRubric(), CancellationToken.None);
        await CreateController(uow, Guid.NewGuid()).Submit(TestHelpers.SampleRubric(), CancellationToken.None);

        var result = await CreateController(uow, mine).List(page: 1, pageSize: 20, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<PagedResultDto<RubricSummaryDto>>(ok.Value);

        Assert.Equal(1, page.TotalCount);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var controller = CreateController(uow, userId);

        var submitted = await controller.Submit(TestHelpers.SampleRubric(), CancellationToken.None);
        var rubric = Assert.IsType<RubricResponseDto>(
            Assert.IsType<CreatedAtActionResult>(submitted.Result).Value);

        var result = await controller.Delete(rubric.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(uow.RubricRows);
    }
}
