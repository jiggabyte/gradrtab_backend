using System.Text.Json;
using GradrTab.Configuration;
using GradrTab.DTOs;
using GradrTab.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace GradrTab.Services;

public sealed class AiDetectionService : IAiDetectionService
{
    public const string SystemPrompt =
        """
        You are a forensic linguist specialising in AI-generated text detection.
        You are given the text of a student's assessment. Estimate how much of it reads as AI-generated versus human-written.

        Weigh this evidence, not vibes:
        - Burstiness: human writing mixes short and long sentences, fragments and asides; model output is metronomic with uniform length and cadence.
        - Vocabulary fingerprints: stock AI diction ("delve", "tapestry", "moreover", "furthermore", "in today's fast-paced world"), hedged synonyms and buzzword density versus concrete lived-in detail.
        - Structural tells: rigid scaffolding, every paragraph opening with a topic sentence and closing with a recap, lists of exactly three items, parallel headings.
        - Discourse tells: generic transitions, conclusions restating the introduction, zero self-correction, zero idiosyncrasy, no personal stakes or named particulars.
        - Human tells that push the score DOWN: typos and repairs, register shifts, hyper-specific anecdotes with names and dates, jargon used informally, emotional unevenness.

        Calibration rules:
        - Judge only the text in front of you. Never invent authorship facts.
        - Score the whole text. Mixed texts (human draft polished by AI, or AI draft edited by a human) land in the middle, never 0 or 100.
        - Short or thin texts carry little evidence: keep confidence LOW and pull aiPercentage toward 50.
        - Templated human writing (cover letters, lab boilerplate) can mimic AI style; say so in the summary instead of scoring 100.
        - aiPercentage + humanPercentage must equal 100. confidence (0-100) is how sure you are of YOUR estimate.
        - verdict is one of: "likely-human" (aiPercentage below 30), "mixed" (30 to 70), "likely-ai" (above 70).
        - Give 2 to 5 signals, each tied to a pattern you actually observed. Weight is that signal's share (0-100) of the verdict.

        Reply with one JSON object and nothing else. No prose, no markdown fence.
        {
          "aiPercentage": 72,
          "humanPercentage": 28,
          "verdict": "likely-ai",
          "confidence": 68,
          "summary": "Two or three sentences on what drove the score.",
          "signals": [
            { "signal": "Uniform cadence", "detail": "Observed pattern with an example.", "weight": 40 }
          ]
        }
        """;

    private static readonly JsonSerializerOptions ParseOptions = new() { PropertyNameCaseInsensitive = true };

    private const int MinimumTextCharacters = 200;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAiService _aiService;
    private readonly AiOptions _aiOptions;
    private readonly ILogger<AiDetectionService> _logger;

    public AiDetectionService(
        IUnitOfWork unitOfWork,
        IAiService aiService,
        IOptions<AiOptions> options,
        ILogger<AiDetectionService> logger)
    {
        _unitOfWork = unitOfWork;
        _aiService = aiService;
        _aiOptions = options.Value;
        _logger = logger;
    }

    public async Task<AiDetectionResult> DetectForDocumentAsync(
        Guid documentId,
        AiDetectionRequestDto request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var document = await _unitOfWork.Documents
            .GetOwnedAsync(documentId, userId, cancellationToken: cancellationToken);

        if (document is null)
        {
            return AiDetectionResult.Failure(
                $"No document with id {documentId} was found.",
                StatusCodes.Status404NotFound);
        }

        var ready = RequireText(document.ExtractedText, document.OriginalFileName);
        if (ready.Failure is not null)
        {
            return AiDetectionResult.Failure(ready.Failure, StatusCodes.Status422UnprocessableEntity);
        }

        return await DetectAsync(ready.Text!, request, userId, document.Id, null, cancellationToken);
    }

    public async Task<AiDetectionResult> DetectForSubmissionAsync(
        Guid submissionId,
        AiDetectionRequestDto request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var submission = await _unitOfWork.Submissions
            .GetOwnedAsync(submissionId, userId, cancellationToken: cancellationToken);

        if (submission is null)
        {
            return AiDetectionResult.Failure(
                $"No submission with id {submissionId} was found.",
                StatusCodes.Status404NotFound);
        }

        if (submission.DocumentId is null)
        {
            return AiDetectionResult.Failure(
                "This submission has no uploaded document, so there is no text to analyse.",
                StatusCodes.Status422UnprocessableEntity);
        }

        var document = await _unitOfWork.Documents
            .GetOwnedAsync(submission.DocumentId.Value, userId, cancellationToken: cancellationToken);

        if (document is null || string.IsNullOrWhiteSpace(document.ExtractedText))
        {
            return AiDetectionResult.Failure(
                "The document this submission was filed with is gone, or nothing could be read out of it.",
                StatusCodes.Status422UnprocessableEntity);
        }

        var ready = RequireText(document.ExtractedText, document.OriginalFileName);
        if (ready.Failure is not null)
        {
            return AiDetectionResult.Failure(ready.Failure, StatusCodes.Status422UnprocessableEntity);
        }

        return await DetectAsync(ready.Text!, request, userId, document.Id, submission.Id, cancellationToken);
    }

    public Task<AiDetectionResult> DetectForTextAsync(
        string text,
        AiDetectionRequestDto request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var ready = RequireText(text, null);
        if (ready.Failure is not null)
        {
            return Task.FromResult(AiDetectionResult.Failure(
                ready.Failure, StatusCodes.Status422UnprocessableEntity));
        }

        return DetectAsync(ready.Text!, request, userId, null, null, cancellationToken);
    }

    private async Task<AiDetectionResult> DetectAsync(
        string text,
        AiDetectionRequestDto request,
        Guid userId,
        Guid? documentId,
        Guid? submissionId,
        CancellationToken cancellationToken)
    {
        var prompt = BuildPrompt(text, request);
        if (prompt.Messages is null)
        {
            return AiDetectionResult.Failure(prompt.Failure!, StatusCodes.Status422UnprocessableEntity);
        }

        var chat = await _aiService.CompleteAsync(
            prompt.Messages,
            request.Model,
            request.Temperature,
            request.MaxTokens,
            endUserId: userId.ToString(),
            cancellationToken);

        if (!chat.Succeeded || chat.Response is null)
        {
            var message = chat.ErrorMessage ?? "The AI provider could not be reached.";
            _logger.LogWarning("AI detection failed with {Status}: {Error}", chat.StatusCode, message);
            return AiDetectionResult.Failure(message, chat.StatusCode ?? StatusCodes.Status502BadGateway);
        }

        var parsed = Parse(chat.Response.Content);
        if (parsed is null)
        {
            const string unreadable = "The model did not return a readable detection.";
            _logger.LogWarning("AI detection failed: {Error}", unreadable);
            return AiDetectionResult.Failure(unreadable, StatusCodes.Status502BadGateway);
        }

        var detection = BuildResponse(parsed, chat.Response, prompt.Truncated, documentId, submissionId);
        _logger.LogInformation("AI detection: {Ai}% AI ({Verdict})", detection.AiPercentage, detection.Verdict);
        return AiDetectionResult.Success(detection);
    }

    private static (string? Text, string? Failure) RequireText(string? text, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, fileName is null
                ? "Send the assessment text to analyse."
                : $"'{fileName}' holds no readable text to analyse.");
        }

        var trimmed = text.Trim();
        if (trimmed.Length < MinimumTextCharacters)
        {
            return (null, $"Too little text to analyse ({trimmed.Length} chars, need {MinimumTextCharacters}).");
        }

        return (trimmed, null);
    }

    private Prompt BuildPrompt(string text, AiDetectionRequestDto request)
    {
        var instructions = string.IsNullOrWhiteSpace(request.Instructions)
            ? string.Empty
            : $"\nEXTRA CONTEXT FROM THE MARKER\n{request.Instructions.Trim()}\n";

        var budget = _aiOptions.MaxRequestCharacters - SystemPrompt.Length - instructions.Length;
        if (budget < MinimumTextCharacters)
        {
            return Prompt.Failed("The instructions leave no room for the assessment text.");
        }

        var truncated = false;
        if (text.Length > budget)
        {
            text = text[..budget] + "\n\n[... cut short ...]";
            truncated = true;
        }

        return Prompt.Ready(
            [new AiChatMessageDto("system", SystemPrompt), new AiChatMessageDto("user", $"TEXT TO ANALYSE\n{text}{instructions}")],
            truncated);
    }

    private static ModelDetection? Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var text = content.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBreak = text.IndexOf('\n');
            var closing = text.IndexOf("```", Math.Max(firstBreak, 0), StringComparison.Ordinal);
            text = firstBreak >= 0 && closing > firstBreak
                ? text[(firstBreak + 1)..closing]
                : text[(Math.Max(firstBreak, 0) + 1)..];
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            text = text[start..(end + 1)];
        }

        ModelDetection? detection;
        try
        {
            detection = JsonSerializer.Deserialize<ModelDetection>(text, ParseOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        return detection?.AiPercentage is null ? null : detection;
    }

    private static AiDetectionResponseDto BuildResponse(
        ModelDetection parsed,
        AiChatResponseDto response,
        bool truncated,
        Guid? documentId,
        Guid? submissionId)
    {
        var ai = Math.Clamp(parsed.AiPercentage!.Value, 0, 100);
        var human = parsed.HumanPercentage is null || Math.Abs(ai + parsed.HumanPercentage.Value - 100) > 0.6
            ? 100 - ai
            : Math.Clamp(parsed.HumanPercentage.Value, 0, 100);

        var raw = (parsed.Verdict ?? string.Empty).Trim().ToLowerInvariant();
        var verdict = raw switch
        {
            "likely-human" or "human" => "likely-human",
            "likely-ai" or "ai" or "likely_ai" or "likely ai" => "likely-ai",
            "mixed" => "mixed",
            _ => ai < 30 ? "likely-human" : ai > 70 ? "likely-ai" : "mixed",
        };

        var signals = (parsed.Signals ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Signal))
            .Select(s => new AiDetectionSignalDto(
                s.Signal!.Trim(),
                string.IsNullOrWhiteSpace(s.Detail) ? string.Empty : s.Detail.Trim(),
                Math.Clamp(s.Weight ?? 0, 0, 100)))
            .ToList();

        return new AiDetectionResponseDto(
            documentId,
            submissionId,
            Math.Round(ai, 1),
            Math.Round(human, 1),
            verdict,
            parsed.Confidence is null ? 50 : Math.Round(Math.Clamp(parsed.Confidence.Value, 0, 100), 1),
            string.IsNullOrWhiteSpace(parsed.Summary) ? null : parsed.Summary.Trim(),
            signals,
            truncated,
            response.Model,
            DateTime.UtcNow);
    }

    private readonly record struct Prompt(
        IReadOnlyList<AiChatMessageDto>? Messages,
        bool Truncated,
        string? Failure)
    {
        public static Prompt Ready(IReadOnlyList<AiChatMessageDto> messages, bool truncated) =>
            new(messages, truncated, null);

        public static Prompt Failed(string failure) =>
            new(null, false, failure);
    }

    private sealed class ModelDetection
    {
        public double? AiPercentage { get; set; }

        public double? HumanPercentage { get; set; }

        public string? Verdict { get; set; }

        public double? Confidence { get; set; }

        public string? Summary { get; set; }

        public List<ModelSignal>? Signals { get; set; }
    }

    private sealed class ModelSignal
    {
        public string? Signal { get; set; }

        public string? Detail { get; set; }

        public double? Weight { get; set; }
    }
}
