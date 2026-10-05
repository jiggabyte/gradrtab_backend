using GradrTab.DTOs;
using GradrTab.Repositories;

namespace GradrTab.Services;

// CRUD over the rubrics a user owns. Every call is scoped to the signed in user
// and the rubric is stored as it was submitted, the server only owns the owner
// and the timestamps.
public interface IRubricService
{
    Task<RubricResponseDto> CreateAsync(
        RubricWriteDto rubric,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<RubricResponseDto?> GetByIdAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<PagedResultDto<RubricSummaryDto>> ListAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<RubricWriteResult> UpdateAsync(
        Guid id,
        RubricWriteDto rubric,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<RubricWriteResult> DeleteAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default);
}

// Outcome of an update or a delete. A rubric owned by somebody else is reported
// exactly like one that does not exist, so the api never confirms it is there.
public sealed record RubricWriteResult(bool Succeeded, RubricResponseDto? Rubric)
{
    public static RubricWriteResult Applied(RubricResponseDto rubric) =>
        new(true, rubric);

    public static RubricWriteResult NotFound() =>
        new(false, null);
}
