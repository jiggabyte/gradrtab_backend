using System.Text.Json;
using GradrTab.DTOs;
using GradrTab.Models;
using GradrTab.Tests.Support;
using Microsoft.AspNetCore.Mvc;

namespace GradrTab.Tests;

public class GradingEmailTests
{
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web);

    private static (InMemoryUnitOfWork uow, Guid userId, Guid submissionId, FakeEmailSender mail) SetupGraded()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var seeded = TestHelpers.SeedGradableSubmission(uow, userId);

        var evaluation = new EvaluationDto(
            seeded.SubmissionId,
            seeded.Rubric.Id,
            seeded.Submission.DocumentId,
            "S-1001",
            "Ada Lovelace",
            "Economics",
            "ECON-101",
            88,
            100,
            88,
            "B+",
            "Solid work.",
            [new CriterionEvaluationDto("Thesis", 35, 40, "Strong claim.")],
            ["Clear structure"],
            ["Add citations"],
            false,
            "openai/gpt-4o-mini",
            DateTime.UtcNow);

        seeded.Submission.Evaluation = JsonSerializer.Serialize(evaluation, WriteOptions);
        seeded.Submission.GradingStatus = GradeState.Complete;

        var mail = new FakeEmailSender();
        return (uow, userId, seeded.SubmissionId, mail);
    }

    [Fact]
    public async Task SendGradedSubmission_MailsSubmissionRubricAndEvaluation()
    {
        var (uow, userId, submissionId, mail) = SetupGraded();
        var service = TestHelpers.CreateGradingEmailService(uow, mail);

        var result = await service.SendGradingResultAsync(submissionId, "grader@example.com", userId);

        Assert.True(result.Succeeded);
        var sent = Assert.Single(mail.Sent);
        Assert.Equal("grader@example.com", sent.To);
        Assert.Contains("Ada Lovelace", sent.HtmlBody);
        Assert.Contains("S-1001", sent.HtmlBody);
        Assert.Contains("ECON-101", sent.HtmlBody);
        Assert.Contains("Criteria: Thesis 40", sent.HtmlBody);
        Assert.Contains("B+", sent.HtmlBody);
        Assert.Contains("88", sent.HtmlBody);
        Assert.Contains(submissionId.ToString(), sent.HtmlBody);
        Assert.Contains("Ada Lovelace", sent.TextBody);
        Assert.Contains("Criteria: Thesis 40", sent.TextBody);
    }

    [Fact]
    public async Task SendWithoutGrade_AnswersUnprocessable()
    {
        var uow = new InMemoryUnitOfWork();
        var userId = Guid.NewGuid();
        var seeded = TestHelpers.SeedGradableSubmission(uow, userId);
        var service = TestHelpers.CreateGradingEmailService(uow, new FakeEmailSender());

        var result = await service.SendGradingResultAsync(seeded.SubmissionId, "grader@example.com", userId);

        Assert.False(result.Succeeded);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task SendWithBadEmail_AnswersBadRequest()
    {
        var (uow, userId, submissionId, mail) = SetupGraded();
        var service = TestHelpers.CreateGradingEmailService(uow, mail);

        var result = await service.SendGradingResultAsync(submissionId, "not-an-email", userId);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.StatusCode);
        Assert.Empty(mail.Sent);
    }

    [Fact]
    public async Task SendForMissingSubmission_AnswersNotFound()
    {
        var (uow, userId, _, mail) = SetupGraded();
        var service = TestHelpers.CreateGradingEmailService(uow, mail);

        var result = await service.SendGradingResultAsync(Guid.NewGuid(), "grader@example.com", userId);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task SendWithoutSmtp_AnswersUnavailable()
    {
        var (uow, userId, submissionId, _) = SetupGraded();
        var down = new FakeEmailSender { IsAvailable = false, UnavailableReason = "No SMTP host." };
        var service = TestHelpers.CreateGradingEmailService(uow, down);

        var result = await service.SendGradingResultAsync(submissionId, "grader@example.com", userId);

        Assert.False(result.Succeeded);
        Assert.Equal(503, result.StatusCode);
    }

    [Fact]
    public async Task Controller_MailsAndAnswersOk()
    {
        var (uow, userId, submissionId, mail) = SetupGraded();
        var controller = TestHelpers.CreateSubmissionsController(uow, userId: userId, emailSender: mail);

        var result = await controller.EmailGradingResult(
            submissionId, new GradingEmailRequestDto { GraderEmail = "grader@example.com" }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<GradingEmailResponseDto>(ok.Value);
        Assert.Equal("grader@example.com", body.ToEmail);
        Assert.Single(mail.Sent);
    }

    [Fact]
    public async Task Controller_WithoutUser_AnswersUnauthorized()
    {
        var (uow, _, submissionId, mail) = SetupGraded();
        var controller = TestHelpers.CreateSubmissionsController(uow, emailSender: mail);

        var result = await controller.EmailGradingResult(
            submissionId, new GradingEmailRequestDto { GraderEmail = "grader@example.com" }, CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Controller_WithMissingBody_AnswersBadRequest()
    {
        var (uow, userId, submissionId, _) = SetupGraded();
        var controller = TestHelpers.CreateSubmissionsController(uow, userId: userId);

        var result = await controller.EmailGradingResult(submissionId, null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
