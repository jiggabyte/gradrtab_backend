using GradrTab.DTOs;
using GradrTab.Repositories;

namespace GradrTab.Services;

// Mails the stored grading result of one submission to a grader. The mail
// carries the submission details, the rubric details and the evaluation, so
// the grader never has to open the app to see how the grade came about.
public interface IGradingEmailService
{
    // Loads the submission owned by userId, requires a stored evaluation and
    // sends it together with the submission and rubric details to graderEmail.
    // Problems are reported on the result instead of thrown, so the controller
    // can turn them into 400/404/422/503/502 answers.
    Task<GradingEmailResult> SendGradingResultAsync(
        Guid submissionId,
        string graderEmail,
        Guid userId,
        CancellationToken cancellationToken = default);
}

// Outcome of one mail run. StatusCode tells the controller what to answer:
// 400 for a bad email address, 404 when the submission is not there, 422 when
// it has no grade (or no rubric) to send, 503 when SMTP is not configured and
// 502 when the SMTP server refused the message.
public sealed record GradingEmailResult(
    bool Succeeded,
    string? ToEmail,
    Guid? SubmissionId,
    string? ErrorMessage,
    int? StatusCode)
{
    public static GradingEmailResult Success(string toEmail, Guid submissionId) =>
        new(true, toEmail, submissionId, null, 200);

    public static GradingEmailResult Failure(string message, int statusCode) =>
        new(false, null, null, message, statusCode);
}
