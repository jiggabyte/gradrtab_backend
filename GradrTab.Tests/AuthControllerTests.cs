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
        return (new AuthController(uow, config), uow);
    }

    [Fact]
    public async Task Register_CreatesUser()
    {
        var (controller, uow) = CreateController();

        var result = await controller.Register(new RegisterRequestDto("Ada", "Lovelace", "Ada@Example.com", "password123"));

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var user = Assert.IsType<UserResponseDto>(created.Value);
        Assert.Equal("ada@example.com", user.Email);
        Assert.True(await uow.Users.ExistsWithEmailAsync("ada@example.com"));
    }

    [Fact]
    public async Task Register_RejectsDuplicateEmail()
    {
        var (controller, _) = CreateController();
        await controller.Register(new RegisterRequestDto("Ada", "L", "ada@example.com", "password123"));

        var result = await controller.Register(new RegisterRequestDto("Ada", "L", "ada@example.com", "password123"));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Login_ReturnsTokenForValidCredentials()
    {
        var (controller, _) = CreateController();
        await controller.Register(new RegisterRequestDto("Ada", "L", "ada@example.com", "password123"));

        var result = await controller.Login(new LoginRequestDto("ada@example.com", "password123"));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var auth = Assert.IsType<AuthResponseDto>(ok.Value);
        Assert.False(string.IsNullOrWhiteSpace(auth.Token));
    }

    [Fact]
    public async Task Login_RejectsWrongPassword()
    {
        var (controller, _) = CreateController();
        await controller.Register(new RegisterRequestDto("Ada", "L", "ada@example.com", "password123"));

        var result = await controller.Login(new LoginRequestDto("ada@example.com", "wrong"));

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }
}
