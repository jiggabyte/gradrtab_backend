using System.Security.Cryptography;
using System.Text;
using GradrTab.Configuration;
using GradrTab.Services;
using GradrTab.Tests.Support;
using BC = BCrypt.Net.BCrypt;

namespace GradrTab.Tests;

public class PasswordResetServiceTests
{
    [Fact]
    public async Task RequestReset_MailsALinkAndStoresOnlyItsHash()
    {
        var uow = new InMemoryUnitOfWork();
        var user = TestHelpers.SeedUser(uow);
        var email = new FakeEmailSender();
        var service = TestHelpers.CreatePasswordResetService(uow, email);

        var result = await service.RequestResetAsync(user.Email);

        Assert.True(result.Succeeded);
        Assert.True(result.EmailSent);

        var sent = Assert.Single(email.Sent);
        Assert.Equal(user.Email, sent.To);

        var token = sent.Token;
        Assert.NotNull(token);

        // The link points at the frontend page, not at an API route
        Assert.Contains("https://app.gradrtab.example/reset-password?token=", sent.HtmlBody);

        // The row holds the hash, never the token that was mailed
        var stored = Assert.Single(uow.ResetTokens);
        Assert.NotEqual(token, stored.TokenHash);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
            stored.TokenHash);
        Assert.Null(stored.UsedAt);
        Assert.True(stored.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task RequestReset_AnswersTheSameForAnUnknownAddress()
    {
        var uow = new InMemoryUnitOfWork();
        var email = new FakeEmailSender();
        var service = TestHelpers.CreatePasswordResetService(uow, email);

        var result = await service.RequestResetAsync("nobody@example.com");

        // Accepted, but nothing is sent and nothing is stored
        Assert.True(result.Succeeded);
        Assert.False(result.EmailSent);
        Assert.Empty(email.Sent);
        Assert.Empty(uow.ResetTokens);
    }

    [Fact]
    public async Task RequestReset_ReportsUnavailableWhenSmtpIsOff()
    {
        var uow = new InMemoryUnitOfWork();
        TestHelpers.SeedUser(uow);
        var email = new FakeEmailSender { IsAvailable = false, UnavailableReason = "No SMTP host is configured." };
        var service = TestHelpers.CreatePasswordResetService(uow, email);

        var result = await service.RequestResetAsync("ada@example.com");

        Assert.False(result.Succeeded);
        Assert.Equal(503, result.StatusCode);
        Assert.Contains("No SMTP host", result.ErrorMessage);
        Assert.Empty(uow.ResetTokens);
    }

    [Fact]
    public async Task RequestReset_KeepsOnlyTheNewestLinkUsable()
    {
        var uow = new InMemoryUnitOfWork();
        var user = TestHelpers.SeedUser(uow);
        var email = new FakeEmailSender();
        var service = TestHelpers.CreatePasswordResetService(uow, email);

        await service.RequestResetAsync(user.Email);
        var firstToken = email.LastEmail!.Token;

        await service.RequestResetAsync(user.Email);
        var secondToken = email.LastEmail!.Token;

        Assert.NotEqual(firstToken, secondToken);

        // The first link is burnt the moment a second one is asked for
        var first = await service.CompleteResetAsync(firstToken!, "brandNewPassword1");
        Assert.Equal(PasswordResetStatus.InvalidToken, first.Status);

        var second = await service.CompleteResetAsync(secondToken!, "brandNewPassword1");
        Assert.True(second.Succeeded);
    }

    [Fact]
    public async Task RequestReset_ThrottlesAfterTheHourlyLimit()
    {
        var uow = new InMemoryUnitOfWork();
        var user = TestHelpers.SeedUser(uow);
        var email = new FakeEmailSender();

        var options = TestHelpers.DefaultPasswordResetOptions();
        options.MaxRequestsPerHour = 2;

        var service = TestHelpers.CreatePasswordResetService(uow, email, options);

        await service.RequestResetAsync(user.Email);
        await service.RequestResetAsync(user.Email);
        var third = await service.RequestResetAsync(user.Email);

        // The caller still gets the same accepted answer
        Assert.True(third.Succeeded);
        Assert.False(third.EmailSent);
        Assert.Equal(2, email.Sent.Count);
    }

    [Fact]
    public async Task RequestReset_BurnsTheTokenWhenTheMailCannotBeSent()
    {
        var uow = new InMemoryUnitOfWork();
        var user = TestHelpers.SeedUser(uow);
        var email = new FakeEmailSender { FailNextSend = true };
        var service = TestHelpers.CreatePasswordResetService(uow, email);

        var result = await service.RequestResetAsync(user.Email);

        Assert.False(result.Succeeded);
        Assert.Equal(502, result.StatusCode);

        // The burn has to reach the database, not just the change tracker. Two
        // saves happen on this path: one for the new row, one for the burn.
        Assert.True(uow.SaveCalls >= 2, "The burnt token must be committed, not only staged.");

        // A link nobody received must not stay valid
        var token = email.LastEmail!.Token;
        Assert.NotNull(token);
        var completion = await service.CompleteResetAsync(token!, "brandNewPassword1");
        Assert.Equal(PasswordResetStatus.InvalidToken, completion.Status);
    }

    [Fact]
    public async Task CompleteReset_StoresANewHashAndKillsTheOldPassword()
    {
        var uow = new InMemoryUnitOfWork();
        var user = TestHelpers.SeedUser(uow, password: "oldPassword123");
        var email = new FakeEmailSender();
        var service = TestHelpers.CreatePasswordResetService(uow, email);

        await service.RequestResetAsync(user.Email);
        var result = await service.CompleteResetAsync(email.LastEmail!.Token!, "brandNewPassword1");

        Assert.True(result.Succeeded);

        var stored = await uow.Users.GetByIdAsync(user.Id);
        Assert.True(BC.Verify("brandNewPassword1", stored!.PasswordHash));
        Assert.False(BC.Verify("oldPassword123", stored.PasswordHash));

        // The link cannot be used a second time
        var again = await service.CompleteResetAsync(email.LastEmail!.Token!, "yetAnotherPass1");
        Assert.Equal(PasswordResetStatus.InvalidToken, again.Status);
    }

    [Fact]
    public async Task CompleteReset_RefusesAnUnknownToken()
    {
        var uow = new InMemoryUnitOfWork();
        TestHelpers.SeedUser(uow);
        var service = TestHelpers.CreatePasswordResetService(uow);

        var result = await service.CompleteResetAsync("this-token-was-never-issued", "brandNewPassword1");

        Assert.False(result.Succeeded);
        Assert.Equal(PasswordResetStatus.InvalidToken, result.Status);
    }

    [Fact]
    public async Task CompleteReset_RefusesAnExpiredToken()
    {
        var uow = new InMemoryUnitOfWork();
        var user = TestHelpers.SeedUser(uow);
        var email = new FakeEmailSender();
        var service = TestHelpers.CreatePasswordResetService(uow, email);

        await service.RequestResetAsync(user.Email);

        // The clock ran past the lifetime
        foreach (var stored in uow.ResetTokens)
        {
            stored.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        }

        var result = await service.CompleteResetAsync(email.LastEmail!.Token!, "brandNewPassword1");

        Assert.Equal(PasswordResetStatus.InvalidToken, result.Status);
        Assert.True(BC.Verify("oldPassword123", user.PasswordHash));
    }

    [Fact]
    public async Task CompleteReset_RefusesAPasswordBelowTheMinimumLength()
    {
        var uow = new InMemoryUnitOfWork();
        var user = TestHelpers.SeedUser(uow);
        var email = new FakeEmailSender();
        var service = TestHelpers.CreatePasswordResetService(uow, email);

        await service.RequestResetAsync(user.Email);
        var result = await service.CompleteResetAsync(email.LastEmail!.Token!, "short");

        Assert.Equal(PasswordResetStatus.PasswordTooShort, result.Status);
        Assert.Contains("at least 8", result.ErrorMessage);

        // The link survives a rejected attempt so the user can try again
        var retry = await service.CompleteResetAsync(email.LastEmail!.Token!, "longEnoughPassword");
        Assert.True(retry.Succeeded);
    }

    [Fact]
    public async Task CompleteReset_RefusesTheCurrentPassword()
    {
        var uow = new InMemoryUnitOfWork();
        var user = TestHelpers.SeedUser(uow, password: "oldPassword123");
        var email = new FakeEmailSender();
        var service = TestHelpers.CreatePasswordResetService(uow, email);

        await service.RequestResetAsync(user.Email);
        var result = await service.CompleteResetAsync(email.LastEmail!.Token!, "oldPassword123");

        Assert.Equal(PasswordResetStatus.PasswordUnchanged, result.Status);
    }

    [Fact]
    public async Task CompleteReset_InvalidatesEveryOtherLinkOfTheUser()
    {
        var uow = new InMemoryUnitOfWork();
        var user = TestHelpers.SeedUser(uow);

        // Two live links, which can only happen with a raised rate limit
        var options = TestHelpers.DefaultPasswordResetOptions();
        options.MaxRequestsPerHour = 5;

        var first = new FakeEmailSender();
        await TestHelpers.CreatePasswordResetService(uow, first, options).RequestResetAsync(user.Email);
        var firstToken = first.LastEmail!.Token;

        var second = new FakeEmailSender();
        await TestHelpers.CreatePasswordResetService(uow, second, options).RequestResetAsync(user.Email);
        var secondToken = second.LastEmail!.Token;

        // The second request already burnt the first one
        Assert.Equal(
            PasswordResetStatus.InvalidToken,
            (await TestHelpers.CreatePasswordResetService(uow).CompleteResetAsync(firstToken!, "brandNewPassword1")).Status);

        // And using the second one leaves nothing outstanding
        Assert.True((await TestHelpers.CreatePasswordResetService(uow).CompleteResetAsync(secondToken!, "brandNewPassword1")).Succeeded);
        Assert.All(uow.ResetTokens, token => Assert.NotNull(token.UsedAt));
    }
}
