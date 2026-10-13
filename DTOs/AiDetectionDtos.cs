using System.ComponentModel.DataAnnotations;

namespace GradrTab.DTOs;

// Body of POST /api/v1/documents/{id}/ai-detection and
// POST /api/v1/submissions/{id}/ai-detection. Every field is optional,
// an empty body detects with whatever the Ai section is configured with.
public class AiDetectionRequestDto
{
    // Detects with this model instead of the configured Ai:Model
    [StringLength(200)]
    public string? Model { get; set; }

    // Extra guidance for this one detection, for example
    // "the students are non-native English writers"
    [StringLength(4_000)]
    public string? Instructions { get; set; }

    [Range(0.0, 2.0)]
    public double? Temperature { get; set; }

    [Range(1, 128_000)]
    public int? MaxTokens { get; set; }
}

// Body of POST /api/v1/ai/detect. Sends raw text instead of a stored document.
public class AiDetectTextRequestDto : AiDetectionRequestDto
{
    // The assessment text to analyse
    [Required]
    [StringLength(100_000, MinimumLength = 1)]
    public string Text { get; set; } = string.Empty;
}

// One stylistic signal behind the verdict
public record AiDetectionSignalDto(
    string Signal,
    string Detail,
    double Weight);

// What every AI-detection endpoint answers with. AiPercentage is the headline:
// 0 means fully human, 100 means fully AI-generated. Confidence says how sure
// the model is about its own estimate, it is not a second percentage of AI.
public record AiDetectionResponseDto(
    Guid? DocumentId,
    Guid? SubmissionId,
    double AiPercentage,
    double HumanPercentage,
    string Verdict,
    double Confidence,
    string? Summary,
    IReadOnlyList<AiDetectionSignalDto> Signals,
    bool TextTruncated,
    string Model,
    DateTime DetectedAt);
