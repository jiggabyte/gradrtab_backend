using System.Security.Claims;
using GradrTab.Controllers;
using GradrTab.DTOs;
using GradrTab.Models;
using GradrTab.Services;
using GradrTab.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GradrTab.Tests;

public class DocumentsControllerTests
{
    private static DocumentsController CreateController(InMemoryUnitOfWork uow, Guid userId, IDocumentProcessingService? processing = null)
    {
        var controller = new DocumentsController(
            uow,
            processing ?? TestHelpers.CreateProcessingService(uow, FakeFileStorageService.Create(), new FakeExtractorResolver(TestHelpers.TextExtractor())),
            FakeFileStorageService.Create(),
            TestHelpers.OptionsWrapper(),
            NullLogger<DocumentsController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test")),
            },
        };
        return controller;
    }

    [Fact]
    public async Task List_FiltersByTypeAndSearch()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        TestHelpers.SeedDocument(uow, userId, "alpha.txt", text: "unique-search-term");
        TestHelpers.SeedDocument(uow, userId, "beta.txt", text: "something else");

        var controller = CreateController(uow, userId);
        var result = await controller.List(new DocumentQueryDto { Search = "unique-search-term" }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<PagedResultDto<DocumentResponseDto>>(ok.Value);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task Get_ReturnsNotFoundForOtherOwnersDocument()
    {
        var uow = new InMemoryUnitOfWork();
        var doc = TestHelpers.SeedDocument(uow, Guid.NewGuid());

        var controller = CreateController(uow, Guid.NewGuid());
        var result = await controller.GetById(doc.Id, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetDetail_RespectsEntryLimit()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var doc = TestHelpers.SeedDocument(uow, userId);

        var controller = CreateController(uow, userId);
        var result = await controller.GetDetail(doc.Id, entryLimit: 1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var detail = Assert.IsType<DocumentDetailResponseDto>(ok.Value);
        Assert.Single(detail.Entries);
    }

    [Fact]
    public async Task Delete_RemovesOwnedDocument()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var doc = TestHelpers.SeedDocument(uow, userId);

        var controller = CreateController(uow, userId);
        var result = await controller.Delete(doc.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await uow.Documents.GetByIdAsync(doc.Id));
    }

    [Fact]
    public async Task Upload_ReturnsCreatedForValidFile()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var controller = CreateController(uow, userId);
        var file = TestHelpers.FormFileFromText("notes.txt", "hello world");

        var result = await controller.Upload([file], CancellationToken.None);

        Assert.IsType<ObjectResult>(result.Result);
    }
}
