using GradrTab.Configuration;
using GradrTab.DTOs;
using GradrTab.Models;
using GradrTab.Repositories;
using Microsoft.Extensions.Options;

namespace GradrTab.Services;

// Turns a submitted rubric into stored rows. The rubric key and the criterion
// keys have to stay unique inside one rubric, and the levels of a criterion have
// to be ordered from the highest to the lowest points, which is the order a
// marker expects to read them in.
public sealed class RubricService : IRubricService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly RubricOptions _options;
    private readonly ILogger<RubricService> _logger;

    public RubricService(
        IUnitOfWork unitOfWork,
        IOptions<RubricOptions> options,
        ILogger<RubricService> logger)
    {
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RubricSubmissionResult> SubmitAsync(
        RubricSubmissionDto submission,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var key = submission.Id.Trim();

        // Validate before touching anything, a rejected resubmission must never
        // cost the user the rubric they already stored
        var errors = Validate(submission);

        if (errors.Count > 0)
        {
            return RubricSubmissionResult.Invalid(errors);
        }

        // A second submission with the same key replaces the old rubric rather
        // than quietly creating a duplicate the frontend cannot tell apart
        var existing = await _unitOfWork.Rubrics.GetByKeyAsync(key, userId, cancellationToken);

        if (existing is not null)
        {
            // Delete and commit first. The insert below reuses the same id, and a
            // single save would batch the insert ahead of the delete and collide
            // on the primary key. The delete is also what takes the old criteria
            // and levels with it, the configured cascade does the rest.
            _unitOfWork.Rubrics.Remove(existing);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var rubric = Build(submission, key, userId);

        if (existing is not null)
        {
            // Keep the identity so the frontend keeps addressing the same rubric
            rubric.Id = existing.Id;
            rubric.CreatedAt = existing.CreatedAt;
        }

        await _unitOfWork.Rubrics.AddAsync(rubric, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Stored rubric {RubricKey} with {Criteria} criteria worth {MaxPoints} points",
            key, rubric.CriterionCount, rubric.MaxPoints);

        return RubricSubmissionResult.Success(ToResponse(rubric));
    }

    // Rules that plain data annotations cannot express, such as "unique inside
    // this payload" and "descending points"
    private List<string> Validate(RubricSubmissionDto submission)
    {
        var errors = new List<string>();

        if (submission.Criteria.Count > _options.MaxCriteriaPerRubric)
        {
            errors.Add($"A rubric may hold at most {_options.MaxCriteriaPerRubric} criteria, this one has {submission.Criteria.Count}.");
        }

        var seenCriteria = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < submission.Criteria.Count; index++)
        {
            var criterion = submission.Criteria[index];
            var label = $"criteria[{index}]";

            if (string.IsNullOrWhiteSpace(criterion.Id))
            {
                errors.Add($"{label} is missing an id.");
            }
            else if (!seenCriteria.Add(criterion.Id.Trim()))
            {
                errors.Add($"{label} reuses the id '{criterion.Id}', criterion ids must be unique.");
            }

            if (string.IsNullOrWhiteSpace(criterion.Name))
            {
                errors.Add($"{label} is missing a name.");
            }

            if (criterion.Levels.Count == 0)
            {
                errors.Add($"{label} has no levels, at least one is needed to score against.");
                continue;
            }

            if (criterion.Levels.Count > _options.MaxLevelsPerCriterion)
            {
                errors.Add($"{label} has {criterion.Levels.Count} levels, at most {_options.MaxLevelsPerCriterion} are allowed.");
            }

            var seenLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var levelIndex = 0; levelIndex < criterion.Levels.Count; levelIndex++)
            {
                var level = criterion.Levels[levelIndex];
                var levelLabel = $"{label}.levels[{levelIndex}]";

                if (string.IsNullOrWhiteSpace(level.Label))
                {
                    errors.Add($"{levelLabel} is missing a label.");
                }
                else if (!seenLabels.Add(level.Label.Trim()))
                {
                    errors.Add($"{levelLabel} reuses the label '{level.Label}', level labels must be unique inside a criterion.");
                }

                if (level.Points <= 0)
                {
                    errors.Add($"{levelLabel} must be worth at least 1 point, it is {level.Points}.");
                }

                // The first level is expected to be the best one
                if (levelIndex > 0 && criterion.Levels[levelIndex - 1].Points <= level.Points)
                {
                    errors.Add(
                        $"{levelLabel} is worth {level.Points} points, which is not less than the " +
                        $"previous level ({criterion.Levels[levelIndex - 1].Points}). List the levels " +
                        "from the highest points to the lowest.");
                }
            }
        }

        return errors;
    }

    // Copies the payload onto new rows and derives the point totals, so a client
    // never has to send them
    private static Rubric Build(RubricSubmissionDto submission, string key, Guid? userId)
    {
        var rubric = new Rubric
        {
            RubricKey = key,
            Title = submission.Title.Trim(),
            UploadedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        for (var index = 0; index < submission.Criteria.Count; index++)
        {
            var source = submission.Criteria[index];

            var criterion = new RubricCriterion
            {
                RubricId = rubric.Id,
                CriterionKey = source.Id.Trim(),
                Name = source.Name.Trim(),
                Order = index
            };

            for (var levelIndex = 0; levelIndex < source.Levels.Count; levelIndex++)
            {
                var sourceLevel = source.Levels[levelIndex];

                criterion.Levels.Add(new RubricLevel
                {
                    CriterionId = criterion.Id,
                    Label = sourceLevel.Label.Trim(),
                    Points = sourceLevel.Points,
                    Description = string.IsNullOrWhiteSpace(sourceLevel.Description)
                        ? null
                        : sourceLevel.Description.Trim(),
                    Order = levelIndex
                });
            }

            // The top band decides what this criterion is worth
            criterion.MaxPoints = criterion.Levels.Max(level => level.Points);
            criterion.RubricId = rubric.Id;
            rubric.Criteria.Add(criterion);
        }

        rubric.CriterionCount = rubric.Criteria.Count;
        rubric.MaxPoints = rubric.Criteria.Sum(criterion => criterion.MaxPoints);

        return rubric;
    }

    public async Task<RubricResponseDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rubric = await _unitOfWork.Rubrics.GetWithChildrenAsync(id, cancellationToken);

        return rubric is null ? null : ToResponse(rubric);
    }

    public async Task<RubricResponseDto?> GetByKeyAsync(
        string rubricKey,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        var rubric = await _unitOfWork.Rubrics.GetWithChildrenByKeyAsync(rubricKey, userId, cancellationToken);

        return rubric is null ? null : ToResponse(rubric);
    }

    public async Task<PagedResultDto<RubricSummaryDto>> ListAsync(
        Guid? userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = userId is null
            ? _unitOfWork.Rubrics.Query(asNoTracking: true)
            : _unitOfWork.Rubrics.Query(asNoTracking: true).Where(r => r.UploadedByUserId == userId);

        var total = await query.CountAsyncCompat(cancellationToken);
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsyncCompat(cancellationToken);

        var summaries = items
            .Select(r => new RubricSummaryDto(
                r.Id, r.RubricKey, r.Title, r.MaxPoints, r.CriterionCount, r.CreatedAt))
            .ToList();

        var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);

        return new PagedResultDto<RubricSummaryDto>(summaries, page, pageSize, total, totalPages);
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        // Tracked, otherwise the delete would not reach the database
        var rubric = await _unitOfWork.Rubrics.GetTrackedAsync(id, cancellationToken);

        if (rubric is null || (userId is not null && rubric.UploadedByUserId != userId))
        {
            return false;
        }

        _unitOfWork.Rubrics.Remove(rubric);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }

    // Always returns the criteria and levels in the submitted order
    private static RubricResponseDto ToResponse(Rubric rubric) => new(
        rubric.Id,
        rubric.RubricKey,
        rubric.Title,
        rubric.MaxPoints,
        rubric.CriterionCount,
        rubric.Criteria
            .OrderBy(c => c.Order)
            .Select(c => new RubricCriterionResponseDto(
                c.CriterionKey,
                c.Name,
                c.MaxPoints,
                c.Levels
                    .OrderBy(l => l.Order)
                    .Select(l => new RubricLevelResponseDto(l.Label, l.Points, l.Description))
                    .ToList()))
            .ToList(),
        rubric.UploadedByUserId,
        rubric.CreatedAt,
        rubric.UpdatedAt);
}
