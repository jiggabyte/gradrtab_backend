using System.Text.Json;
using GradrTab.DTOs;
using GradrTab.Models;
using GradrTab.Repositories;

namespace GradrTab.Services;

// CRUD over a single submission row. The submission is stored exactly as it was
// submitted, only the owner, the grading state and the timestamps are filled in
// by the server.
public sealed class SubmissionService : ISubmissionService
{
    // The stored jsonb was written with the camel cased web naming
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SubmissionService> _logger;

    public SubmissionService(IUnitOfWork unitOfWork, ILogger<SubmissionService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<SubmissionResponseDto> CreateAsync(
        SubmissionWriteDto submission,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var row = new Submission
        {
            RubricId = submission.RubricId,
            DocumentId = submission.DocumentId,
            StudentId = submission.StudentId.Trim(),
            StudentName = submission.StudentName.Trim(),
            Evaluation = TrimOrNull(submission.Evaluation),
            // Every submission starts out ungraded
            GradingStatus = GradeState.NotStarted,
            UploadedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _unitOfWork.Submissions.AddAsync(row, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Stored submission {SubmissionId} for student {StudentId} against rubric {RubricId}",
            row.Id, row.StudentId, row.RubricId);

        return ToResponse(row);
    }

    public async Task<SubmissionResponseDto?> GetByIdAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var submission = await _unitOfWork.Submissions.GetOwnedAsync(id, userId, cancellationToken: cancellationToken);

        return submission is null ? null : ToResponse(submission);
    }

    public async Task<PagedResultDto<SubmissionSummaryDto>> ListAsync(
        Guid userId,
        Guid? rubricId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _unitOfWork.Submissions.OwnedBy(userId);

        if (rubricId is not null)
        {
            query = query.Where(s => s.RubricId == rubricId);
        }

        var totalCount = await query.CountAsyncCompat(cancellationToken);
        var items = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsyncCompat(cancellationToken);

        var summaries = items
            .Select(s => new SubmissionSummaryDto(
                s.Id,
                s.RubricId,
                s.DocumentId,
                s.StudentId,
                s.StudentName,
                s.GradingStatus.ToString(),
                s.CreatedAt,
                s.UpdatedAt))
            .ToList();

        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        return new PagedResultDto<SubmissionSummaryDto>(summaries, page, pageSize, totalCount, totalPages);
    }

    public async Task<SubmissionWriteResult> UpdateAsync(
        Guid id,
        SubmissionWriteDto submission,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Tracked, otherwise the change would never reach the database
        var row = await _unitOfWork.Submissions.GetOwnedTrackedAsync(id, userId, cancellationToken);

        // A submission owned by somebody else reads as missing, the api should
        // not tell apart "no such submission" from "not yours"
        if (row is null)
        {
            return SubmissionWriteResult.NotFound();
        }

        row.RubricId = submission.RubricId;
        row.DocumentId = submission.DocumentId;
        row.StudentId = submission.StudentId.Trim();
        row.StudentName = submission.StudentName.Trim();
        row.Evaluation = TrimOrNull(submission.Evaluation);
        row.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Submissions.Update(row);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Updated submission {SubmissionId} for student {StudentId}",
            row.Id, row.StudentId);

        return SubmissionWriteResult.Applied(ToResponse(row));
    }

    public async Task<SubmissionWriteResult> DeleteAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Tracked, otherwise the delete would not reach the database
        var row = await _unitOfWork.Submissions.GetOwnedTrackedAsync(id, userId, cancellationToken);

        if (row is null)
        {
            return SubmissionWriteResult.NotFound();
        }

        // Read before the row goes away, the caller logs what it deleted
        var deleted = ToResponse(row);

        _unitOfWork.Submissions.Remove(row);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Deleted submission {SubmissionId} for student {StudentId}",
            deleted.Id, deleted.StudentId);

        return SubmissionWriteResult.Applied(deleted);
    }

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // The evaluation is stored as jsonb text and handed back as the object it
    // is. Content that cannot be read is reported as no evaluation rather than
    // taking the whole read down, the same way DocumentMapper treats jsonb.
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

    private static SubmissionResponseDto ToResponse(Submission submission) => new(
        submission.Id,
        submission.RubricId,
        submission.DocumentId,
        submission.StudentId,
        submission.StudentName,
        ReadEvaluation(submission),
        submission.GradingStatus.ToString(),
        submission.FailureReason,
        submission.UploadedByUserId,
        submission.CreatedAt,
        submission.UpdatedAt);
}
