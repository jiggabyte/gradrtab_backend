using GradrTab.DTOs;
using GradrTab.Repositories;

namespace GradrTab.Services;

// CRUD over the submissions a user owns. Every call is scoped to the signed in
// user, the server owns the grading state and the timestamps.
public interface ISubmissionService
{
    Task<SubmissionResponseDto> CreateAsync(
        SubmissionWriteDto submission,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<SubmissionResponseDto?> GetByIdAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default);

    // rubricId narrows the list down to one rubric, null lists them all
    Task<PagedResultDto<SubmissionSummaryDto>> ListAsync(
        Guid userId,
        Guid? rubricId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<SubmissionWriteResult> UpdateAsync(
        Guid id,
        SubmissionWriteDto submission,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<SubmissionWriteResult> DeleteAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default);
}

// Outcome of an update or a delete. A submission owned by somebody else is
// reported exactly like one that does not exist, so the api never confirms that
// it is there.
public sealed record SubmissionWriteResult(bool Succeeded, SubmissionResponseDto? Submission)
{
    public static SubmissionWriteResult Applied(SubmissionResponseDto submission) =>
        new(true, submission);

    public static SubmissionWriteResult NotFound() =>
        new(false, null);
}
