using System.ComponentModel.DataAnnotations;

namespace GradrTab.DTOs;

// Body of POST /api/v1/rubrics. The shape mirrors the payload the frontend
// submits: { "id", "title", "criteria": [ { "id", "name", "levels": [...] } ] }
public class RubricSubmissionDto
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Id { get; set; } = string.Empty;

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    public List<RubricCriterionDto> Criteria { get; set; } = [];
}

public class RubricCriterionDto
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Id { get; set; } = string.Empty;

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    public List<RubricLevelDto> Levels { get; set; } = [];
}

public class RubricLevelDto
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Label { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int Points { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }
}

// What the server stored, including the values it derived itself
public record RubricResponseDto(
    Guid Id,
    string RubricKey,
    string Title,
    int MaxPoints,
    int CriterionCount,
    IReadOnlyList<RubricCriterionResponseDto> Criteria,
    Guid? UploadedByUserId,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record RubricCriterionResponseDto(
    string CriterionKey,
    string Name,
    int MaxPoints,
    IReadOnlyList<RubricLevelResponseDto> Levels);

public record RubricLevelResponseDto(
    string Label,
    int Points,
    string? Description);
