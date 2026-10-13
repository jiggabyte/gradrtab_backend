using GradrTab.DTOs;
using GradrTab.Repositories;

namespace GradrTab.Services;

// Grades the assessment of one submission against the rubric it was filed with.
public interface IGradingService
{
    // Reads the uploaded document of the submission, grades it with the rubric
    // as the guide, stores the evaluation on the submission and returns it.
    // Problems are reported on the result instead of thrown, so one submission
    // that cannot be graded never fails the request.
    Task<GradingResult> GradeAsync(
        Guid submissionId,
        GradingRequestDto request,
        Guid userId,
        CancellationToken cancellationToken = default);
}

// Outcome of one grading run. StatusCode tells the controller what to answer:
// 404 when the submission is not there, 422 when it cannot be graded as it
// stands and 503/502 when the provider is the problem.
public sealed record GradingResult(
    bool Succeeded,
    EvaluationDto? Evaluation,
    string? ErrorMessage,
    int? StatusCode)
{
    public static GradingResult Success(EvaluationDto evaluation) =>
        new(true, evaluation, null, 200);

    public static GradingResult Failure(string message, int statusCode) =>
        new(false, null, message, statusCode);
}
