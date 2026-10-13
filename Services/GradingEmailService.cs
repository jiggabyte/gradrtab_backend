using System.Net;
using System.Text;
using System.Text.Json;
using GradrTab.DTOs;
using GradrTab.Models;
using GradrTab.Repositories;
using Microsoft.AspNetCore.Http;

namespace GradrTab.Services;

// Mails the stored grading result of one submission to a grader.
public sealed class GradingEmailService : IGradingEmailService
{
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<GradingEmailService> _logger;

    public GradingEmailService(
        IUnitOfWork unitOfWork,
        IEmailSender emailSender,
        ILogger<GradingEmailService> logger)
    {
        _unitOfWork = unitOfWork;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task<GradingEmailResult> SendGradingResultAsync(
        Guid submissionId,
        string graderEmail,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var toEmail = graderEmail?.Trim() ?? string.Empty;

        if (!IsValidEmail(toEmail))
        {
            return GradingEmailResult.Failure(
                "Send a valid grader email address, for example { \"graderEmail\": \"grader@example.com\" }.",
                StatusCodes.Status400BadRequest);
        }

        if (!_emailSender.IsAvailable)
        {
            return GradingEmailResult.Failure(
                _emailSender.UnavailableReason ?? "Email is not available.",
                StatusCodes.Status503ServiceUnavailable);
        }

        var submission = await _unitOfWork.Submissions
            .GetOwnedAsync(submissionId, userId, cancellationToken: cancellationToken);

        if (submission is null)
        {
            return GradingEmailResult.Failure(
                $"No submission with id {submissionId} was found.",
                StatusCodes.Status404NotFound);
        }

        var evaluation = ReadEvaluation(submission);

        if (evaluation is null)
        {
            return GradingEmailResult.Failure(
                "This submission has no grading result to send yet. Grade it first with POST /api/v1/submissions/{id}/grade.",
                StatusCodes.Status422UnprocessableEntity);
        }

        Rubric? rubric = null;
        if (submission.RubricId is not null)
        {
            rubric = await _unitOfWork.Rubrics
                .GetOwnedAsync(submission.RubricId.Value, userId, cancellationToken: cancellationToken);
        }

        if (rubric is null && evaluation.RubricId is not null)
        {
            rubric = await _unitOfWork.Rubrics
                .GetOwnedAsync(evaluation.RubricId.Value, userId, cancellationToken: cancellationToken);
        }

        Document? document = null;
        if (submission.DocumentId is not null)
        {
            document = await _unitOfWork.Documents
                .GetOwnedAsync(submission.DocumentId.Value, userId, cancellationToken: cancellationToken);
        }

        var subject = $"Grading result for {submission.StudentName} ({submission.StudentId})" +
            (string.IsNullOrWhiteSpace(evaluation.CourseId) ? string.Empty : $" - {evaluation.CourseId}");

        var result = await _emailSender.SendAsync(
            toEmail,
            subject,
            BuildHtmlBody(submission, rubric, document, evaluation),
            BuildTextBody(submission, rubric, document, evaluation),
            cancellationToken);

        if (!result.Sent)
        {
            _logger.LogWarning(
                "Grading result mail for submission {SubmissionId} to {Recipient} failed: {Error}",
                submissionId, toEmail, result.ErrorMessage);

            return GradingEmailResult.Failure(
                result.ErrorMessage ?? "The email could not be sent.",
                StatusCodes.Status502BadGateway);
        }

        _logger.LogInformation(
            "Mailed grading result of submission {SubmissionId} to {Recipient}",
            submissionId, toEmail);

        return GradingEmailResult.Success(toEmail, submissionId);
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 320)
        {
            return false;
        }

        try
        {
            var address = new System.Net.Mail.MailAddress(email);
            return string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static EvaluationDto? ReadEvaluation(Submission submission)
    {
        if (string.IsNullOrWhiteSpace(submission.Evaluation))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<EvaluationDto>(submission.Evaluation, ReadOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string BuildHtmlBody(
        Submission submission,
        Rubric? rubric,
        Document? document,
        EvaluationDto evaluation)
    {
        var rows = new StringBuilder();
        foreach (var c in evaluation.Criteria)
        {
            rows.Append("<tr><td style=\"padding:8px;border:1px solid #e2e8f0;\">");
            rows.Append(WebUtility.HtmlEncode(c.Name));
            rows.Append("</td><td style=\"padding:8px;border:1px solid #e2e8f0;text-align:right;white-space:nowrap;\">");
            rows.Append(WebUtility.HtmlEncode(FormatScore(c.Score, c.MaxScore)));
            rows.Append("</td><td style=\"padding:8px;border:1px solid #e2e8f0;\">");
            rows.Append(WebUtility.HtmlEncode(c.Comments ?? string.Empty));
            rows.Append("</td></tr>");
        }
        var strengths = evaluation.Strengths.Count == 0
            ? "<p style=\"color:#616e7c;\">None recorded.</p>"
            : "<ul>" + string.Concat(evaluation.Strengths.Select(s => "<li>" + WebUtility.HtmlEncode(s) + "</li>")) + "</ul>";
        var improvements = evaluation.Improvements.Count == 0
            ? "<p style=\"color:#616e7c;\">None recorded.</p>"
            : "<ul>" + string.Concat(evaluation.Improvements.Select(s => "<li>" + WebUtility.HtmlEncode(s) + "</li>")) + "</ul>";
        var rubricHtml = rubric is null
            ? "<p style=\"color:#616e7c;\">The rubric is no longer stored; scores below are as graded.</p>"
            : "<pre style=\"white-space:pre-wrap;background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:12px;\">" + WebUtility.HtmlEncode(rubric.RubricContent) + "</pre>";
        var course = WebUtility.HtmlEncode(rubric?.CourseName ?? evaluation.CourseName);
        var courseId = WebUtility.HtmlEncode(rubric?.CourseId ?? evaluation.CourseId);
        var student = WebUtility.HtmlEncode(submission.StudentName);
        var studentId = WebUtility.HtmlEncode(submission.StudentId);
        var summary = string.IsNullOrWhiteSpace(evaluation.Summary) ? string.Empty : "<p>" + WebUtility.HtmlEncode(evaluation.Summary) + "</p>";
        var grade = string.IsNullOrWhiteSpace(evaluation.Grade) ? string.Empty : " &middot; Grade " + WebUtility.HtmlEncode(evaluation.Grade);
        var docLine = document is null ? string.Empty : "<br />Source file: " + WebUtility.HtmlEncode(document.OriginalFileName);
        return "<!DOCTYPE html><html><body style=\"font-family:Helvetica,Arial,sans-serif;\">"
            + $"<h1>Grading result for {student} ({studentId})</h1>"
            + $"<p>Submission {submission.Id} &middot; Status {submission.GradingStatus}{docLine}</p>"
            + $"<h2>Submission details</h2><p>Student: {student} ({studentId})<br />Submission id: {submission.Id}<br />Rubric id: {(rubric?.Id ?? submission.RubricId)?.ToString() ?? "none"}<br />Document id: {(submission.DocumentId?.ToString() ?? "none")}{docLine}</p>"
            + $"<h2>Rubric details</h2><p>Course: {course} ({courseId})</p>{rubricHtml}"
            + $"<h2>Evaluation</h2><p><strong>{WebUtility.HtmlEncode(DescribeScore(evaluation))}</strong>{grade}</p>{summary}"
            + "<table style=\"border-collapse:collapse;width:100%;\"><thead><tr><th>Criterion</th><th>Score</th><th>Comments</th></tr></thead><tbody>" + rows.ToString() + "</tbody></table>"
            + "<h3>Strengths</h3>" + strengths + "<h3>To improve</h3>" + improvements
            + $"<p style=\"font-size:12px;color:#616e7c;\">Evaluated with {WebUtility.HtmlEncode(evaluation.Model)} at {evaluation.EvaluatedAt:u}.</p>"
            + "</body></html>";
    }


    private static string BuildTextBody(
        Submission submission,
        Rubric? rubric,
        Document? document,
        EvaluationDto evaluation)
    {
        var lines = new StringBuilder();
        lines.AppendLine($"Grading result for {submission.StudentName} ({submission.StudentId})");
        lines.AppendLine($"Submission: {submission.Id}");
        lines.AppendLine($"Status: {submission.GradingStatus}");
        if (document is not null)
        {
            lines.AppendLine($"Source file: {document.OriginalFileName}");
        }
        lines.AppendLine();
        lines.AppendLine("Rubric");
        lines.AppendLine($"Course: {rubric?.CourseName ?? evaluation.CourseName} ({rubric?.CourseId ?? evaluation.CourseId})");
        if (rubric is not null)
        {
            lines.AppendLine($"Rubric id: {rubric.Id}");
            lines.AppendLine(rubric.RubricContent);
        }
        else
        {
            lines.AppendLine("The rubric is no longer stored; scores below are as graded.");
        }
        lines.AppendLine();
        lines.AppendLine("Evaluation");
        lines.AppendLine(DescribeScore(evaluation));
        if (!string.IsNullOrWhiteSpace(evaluation.Grade))
        {
            lines.AppendLine($"Grade: {evaluation.Grade}");
        }
        if (!string.IsNullOrWhiteSpace(evaluation.Summary))
        {
            lines.AppendLine($"Summary: {evaluation.Summary}");
        }
        foreach (var c in evaluation.Criteria)
        {
            var extra = string.IsNullOrWhiteSpace(c.Comments) ? string.Empty : $" -- {c.Comments}";
            lines.AppendLine($"- {c.Name}: {FormatScore(c.Score, c.MaxScore)}{extra}");
        }
        foreach (var s in evaluation.Strengths)
        {
            lines.AppendLine($"Strength: {s}");
        }
        foreach (var i in evaluation.Improvements)
        {
            lines.AppendLine($"To improve: {i}");
        }
        lines.AppendLine($"Evaluated with {evaluation.Model} at {evaluation.EvaluatedAt:u}.");
        return lines.ToString();
    }

    private static string DescribeScore(EvaluationDto evaluation)
    {
        var score = FormatScore(evaluation.OverallScore, evaluation.MaxScore);
        return evaluation.Percentage is not null
            ? $"{score} ({evaluation.Percentage:0.#}%)"
            : score;
    }

    private static string FormatScore(double? score, double? maxScore)
    {
        var left = score?.ToString("0.#") ?? "n/a";
        return maxScore is > 0 ? $"{left} / {maxScore:0.#}" : left;
    }
}
