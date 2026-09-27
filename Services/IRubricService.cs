using GradrTab.DTOs;
using GradrTab.Models;
using GradrTab.Repositories;

namespace GradrTab.Services;

// Stores the rubrics the frontend submits. Problems are reported on the result
// instead of thrown, so one bad rubric cannot fail a whole batch.
public interface IRubricService
{
    Task<RubricSubmissionResult> SubmitAsync(
        RubricSubmissionDto submission,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<RubricResponseDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<RubricResponseDto?> GetByKeyAsync(
        string rubricKey,
        Guid? userId = null,
        CancellationToken cancellationToken = default);

    Task<PagedResultDto<RubricSummaryDto>> ListAsync(
        Guid? userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Removes one rubric, a null userId deletes any rubric, otherwise the owner
    // has to match. Returns false when there was nothing to delete.
    Task<bool> DeleteAsync(
        Guid id,
        Guid? userId = null,
        CancellationToken cancellationToken = default);
}

public sealed record RubricSubmissionResult(
    bool Succeeded,
    RubricResponseDto? Rubric,
    IReadOnlyList<string> Errors,
    bool Conflict)
{
    public static RubricSubmissionResult Success(RubricResponseDto rubric) =>
        new(true, rubric, [], false);

    public static RubricSubmissionResult Invalid(IReadOnlyList<string> errors) =>
        new(false, null, errors, false);

    public static RubricSubmissionResult Duplicate(string key) =>
        new(false, null, [$"A rubric with the id '{key}' was already submitted."], true);
}

// Compact entry for the list endpoint, without the nested criteria
public record RubricSummaryDto(
    Guid Id,
    string RubricKey,
    string Title,
    int MaxPoints,
    int CriterionCount,
    DateTime CreatedAt);
