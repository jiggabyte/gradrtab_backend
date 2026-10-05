using GradrTab.Controllers;
using GradrTab.DTOs;
using GradrTab.Models;
using GradrTab.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace GradrTab.Tests;

public class AuthControllerTests
{
    private static (AuthController Controller, InMemoryUnitOfWork Uow) CreateController()
    {
        var uow = new InMemoryUnitOfWork();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "TestSecretThatIsLongEnoughForHmacSha256!!",
            ["JwtSettings:Issuer"] = "GradrTab",
            ["JwtSettings:Audience"] = "GradrTabUsers",
            ["JwtSettings:DurationInMinutes"] = "60",
        }).Build();
        return (new AuthController(uow, TestHelpers.CreatePasswordResetService(uow), config), uow);
    }

    [Fact]
    public async Task Register_CreatesUser()
    {
        var (controller, uow) = CreateController();

        var result = await controller.Register(new RegisterRequestDto("Ada", "Lovelace", "Ada@example.com", "password123"));

        // Register answers 200 with the new user and a token, it does not
        // redirect the way a 201 Created would
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var auth = Assert.IsType<AuthResponseDto>(ok.Value);

        Assert.Equal("ada@example.com", auth.User.Email);
        Assert.Equal("Ada", auth.User.FirstName);
        Assert.Equal("Lovelace", auth.User.LastName);
        Assert.False(string.IsNullOrWhiteSpace(auth.Token));
        Assert.True(await uow.Users.ExistsWithEmailAsync("ada@example.com"));
    }

    [Fact]
    public async Task Register_RejectsDuplicateEmail()
    {
        var (controller, _) = CreateController();
        await controller.Register(new RegisterRequestDto("Ada", "Lovelace", "ada@example.com", "password123"));

        var result = await controller.Register(new RegisterRequestDto("Ada", "Lovelace", "ada@example.com", "password123"));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Login_ReturnsTokenForValidCredentials()
    {
        var (controller, _) = CreateController();
        await controller.Register(new RegisterRequestDto("Ada", "Lovelace", "ada@example.com", "password123"));

        var result = await controller.Login(new LoginRequestDto("ada@example.com", "password123"));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var auth = Assert.IsType<AuthResponseDto>(ok.Value);
        Assert.False(string.IsNullOrWhiteSpace(auth.Token));
    }

    [Fact]
    public async Task Login_RejectsWrongPassword()
    {
        var (controller, _) = CreateController();
        await controller.Register(new RegisterRequestDto("Ada", "Lovelace", "ada@example.com", "password123"));

        var result = await controller.Login(new LoginRequestDto("ada@example.com", "wrong"));

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }
}
