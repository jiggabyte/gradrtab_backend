using GradrTab.DTOs;
using GradrTab.Models;
using GradrTab.Repositories;

namespace GradrTab.Services;

// CRUD over a single rubric row. The rubric is stored exactly as it was
// submitted, only the owner and the timestamps are filled in by the server.
public sealed class RubricService : IRubricService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RubricService> _logger;

    public RubricService(IUnitOfWork unitOfWork, ILogger<RubricService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<RubricResponseDto> CreateAsync(
        RubricWriteDto rubric,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var row = new Rubric
        {
            CourseName = rubric.CourseName.Trim(),
            CourseId = rubric.CourseId.Trim(),
            RubricContent = rubric.RubricContent.Trim(),
            UploadedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _unitOfWork.Rubrics.AddAsync(row, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Stored rubric {RubricId} for {CourseId} ({CourseName})",
            row.Id, row.CourseId, row.CourseName);

        return ToResponse(row);
    }

    public async Task<RubricResponseDto?> GetByIdAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var rubric = await _unitOfWork.Rubrics.GetOwnedAsync(id, userId, cancellationToken: cancellationToken);

        return rubric is null ? null : ToResponse(rubric);
    }

    public async Task<PagedResultDto<RubricSummaryDto>> ListAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _unitOfWork.Rubrics.OwnedBy(userId);

        var totalCount = await query.CountAsyncCompat(cancellationToken);
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsyncCompat(cancellationToken);

        var summaries = items
            .Select(r => new RubricSummaryDto(
                r.Id, r.CourseName, r.CourseId, r.CreatedAt, r.UpdatedAt))
            .ToList();

        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        return new PagedResultDto<RubricSummaryDto>(summaries, page, pageSize, totalCount, totalPages);
    }

    public async Task<RubricWriteResult> UpdateAsync(
        Guid id,
        RubricWriteDto rubric,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Tracked, otherwise the change would never reach the database
        var row = await _unitOfWork.Rubrics.GetOwnedTrackedAsync(id, userId, cancellationToken);

        // A rubric owned by somebody else reads as missing, the api should not
        // tell apart "no such rubric" from "not yours"
        if (row is null)
        {
            return RubricWriteResult.NotFound();
        }

        row.CourseName = rubric.CourseName.Trim();
        row.CourseId = rubric.CourseId.Trim();
        row.RubricContent = rubric.RubricContent.Trim();
        row.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Rubrics.Update(row);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Updated rubric {RubricId} for {CourseId} ({CourseName})",
            row.Id, row.CourseId, row.CourseName);

        return RubricWriteResult.Applied(ToResponse(row));
    }

    public async Task<RubricWriteResult> DeleteAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Tracked, otherwise the delete would not reach the database
        var row = await _unitOfWork.Rubrics.GetOwnedTrackedAsync(id, userId, cancellationToken);

        if (row is null)
        {
            return RubricWriteResult.NotFound();
        }

        // Read before the row goes away, the caller logs what it deleted
        var deleted = ToResponse(row);

        _unitOfWork.Rubrics.Remove(row);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Deleted rubric {RubricId} for {CourseId} ({CourseName})",
            deleted.Id, deleted.CourseId, deleted.CourseName);

        return RubricWriteResult.Applied(deleted);
    }

    private static RubricResponseDto ToResponse(Rubric rubric) => new(
        rubric.Id,
        rubric.CourseName,
        rubric.CourseId,
        rubric.RubricContent,
        rubric.UploadedByUserId,
        rubric.CreatedAt,
        rubric.UpdatedAt);
}
