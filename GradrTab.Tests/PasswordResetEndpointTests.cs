using GradrTab.Controllers;
using GradrTab.DTOs;
using GradrTab.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using BC = BCrypt.Net.BCrypt;

namespace GradrTab.Tests;

public class PasswordResetEndpointTests
{
    private static (AuthController Controller, InMemoryUnitOfWork Uow, FakeEmailSender Email) CreateController(
        Action<FakeEmailSender>? configureEmail = null)
    {
        var uow = new InMemoryUnitOfWork();
        var email = new FakeEmailSender();
        configureEmail?.Invoke(email);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "TestSecretThatIsLongEnoughForHmacSha256!!",
            ["JwtSettings:Issuer"] = "GradrTab",
            ["JwtSettings:Audience"] = "GradrTabUsers",
            ["JwtSettings:DurationInMinutes"] = "60",
        }).Build();

        return (new AuthController(uow, TestHelpers.CreatePasswordResetService(uow, email), config), uow, email);
    }

    [Fact]
    public async Task ForgotPassword_AnswersAcceptedAndMailsAKnownAddress()
    {
        var (controller, uow, email) = CreateController();
        var user = TestHelpers.SeedUser(uow);

        var result = await controller.ForgotPassword(new ForgotPasswordRequestDto { Email = user.Email });

        Assert.IsType<AcceptedResult>(result);

        var sent = Assert.Single(email.Sent);
        Assert.Equal(user.Email, sent.To);
        Assert.NotNull(sent.Token);
    }

    [Fact]
    public async Task ForgotPassword_GivesTheSameAnswerForAnUnknownAddress()
    {
        var (controller, uow, email) = CreateController();
        var user = TestHelpers.SeedUser(uow);

        var known = await controller.ForgotPassword(new ForgotPasswordRequestDto { Email = user.Email });
        var unknown = await controller.ForgotPassword(new ForgotPasswordRequestDto { Email = "nobody@example.com" });

        // The status is identical so the endpoint cannot be used to probe for accounts
        Assert.IsType<AcceptedResult>(known);
        Assert.IsType<AcceptedResult>(unknown);

        // Only the real account received a link
        Assert.Single(email.Sent);
    }

    [Fact]
    public async Task ForgotPassword_ReportsAServiceProblemWhenSmtpIsOff()
    {
        var (controller, _, _) = CreateController(email =>
        {
            email.IsAvailable = false;
            email.UnavailableReason = "No SMTP host is configured.";
        });

        var result = await controller.ForgotPassword(new ForgotPasswordRequestDto { Email = "ada@example.com" });

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, objectResult.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_StillAnswersAcceptedWhenTheMailCannotBeDelivered()
    {
        var (controller, _, _) = CreateController(email => email.FailNextSend = true);

        var result = await controller.ForgotPassword(new ForgotPasswordRequestDto { Email = "ada@example.com" });

        // Not a 5xx: the caller cannot tell a delivery failure apart from the
        // accepted answer, so registered addresses stay unguessable.
        Assert.IsType<AcceptedResult>(result);
    }

    [Fact]
    public async Task ResetPassword_ChangesThePasswordSoLoginWorks()
    {
        var (controller, uow, email) = CreateController();
        var user = TestHelpers.SeedUser(uow, password: "oldPassword123");

        await controller.ForgotPassword(new ForgotPasswordRequestDto { Email = user.Email });
        var token = email.LastEmail!.Token!;

        var result = await controller.ResetPassword(new ResetPasswordRequestDto
        {
            Token = token,
            NewPassword = "brandNewPassword1",
            ConfirmPassword = "brandNewPassword1"
        });

        Assert.IsType<OkObjectResult>(result);

        var stored = await uow.Users.GetByIdAsync(user.Id);
        Assert.True(BC.Verify("brandNewPassword1", stored!.PasswordHash));
    }

    [Fact]
    public async Task ResetPassword_ReturnsBadRequestForAnUnknownToken()
    {
        var (controller, _, _) = CreateController();

        var result = await controller.ResetPassword(new ResetPasswordRequestDto
        {
            Token = "never-issued-token",
            NewPassword = "brandNewPassword1",
            ConfirmPassword = "brandNewPassword1"
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("invalid or has expired", badRequest.Value!.ToString());
    }
}
