using GradrTab.DTOs;

namespace GradrTab.Services;

// Sends the text of an assessment document to the AI and asks it to judge how
// much of the text looks AI-generated, answering with a percentage.
// This is a stylistic estimate, never proof: a high score means the text reads
// like model output, not that a model wrote it.
public interface IAiDetectionService
{
    // Analyses the stored document owned by userId
    Task<AiDetectionResult> DetectForDocumentAsync(
        Guid documentId,
        AiDetectionRequestDto request,
        Guid userId,
        CancellationToken cancellationToken = default);

    // Analyses the uploaded document behind the submission owned by userId
    Task<AiDetectionResult> DetectForSubmissionAsync(
        Guid submissionId,
        AiDetectionRequestDto request,
        Guid userId,
        CancellationToken cancellationToken = default);

    // Analyses raw text sent in the request body
    Task<AiDetectionResult> DetectForTextAsync(
        string text,
        AiDetectionRequestDto request,
        Guid userId,
        CancellationToken cancellationToken = default);
}

// Outcome of one detection run. StatusCode tells the controller what to answer:
// 404 when the document/submission is not there, 422 when there is no readable
// text to analyse, and 503/502 when the provider is the problem.
public sealed record AiDetectionResult(
    bool Succeeded,
    AiDetectionResponseDto? Detection,
    string? ErrorMessage,
    int? StatusCode)
{
    public static AiDetectionResult Success(AiDetectionResponseDto detection) =>
        new(true, detection, null, 200);

    public static AiDetectionResult Failure(string message, int statusCode) =>
        new(false, null, message, statusCode);
}
