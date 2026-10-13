using System.Text.Json;
using GradrTab.Configuration;
using GradrTab.DTOs;
using GradrTab.Models;
using GradrTab.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace GradrTab.Services;

// Grades the assessment of a submission against the rubric it was filed with.
// The rubric is the guide and the model is only asked to apply it, whatever it
// answers is written back onto the submission so it can be read without asking
// for a second grade.
public sealed class GradingService : IGradingService
{
    // Sent as the system message, it also fixes the shape of the answer
    private const string SystemPrompt =
        """
        You are a strict but fair grader. You are given a rubric and the text of a student's assessment. Grade the assessment against that rubric and nothing else.

        Rules:
        - Score every criterion the rubric lists and stay inside the point range it gives.
        - Judge only what the student actually wrote, never invent quotes or claims.
        - If the rubric does not state the points, work the total out from the criteria you scored.
        - Comments have to be specific to this assessment, not generic advice.
        - Reply with one JSON object and nothing else. No prose, no markdown fence.

        The object looks like this:
        {
          "overallScore": 88,
          "maxScore": 100,
          "grade": "B+",
          "summary": "Two or three sentences on how the assessment did.",
          "criteria": [
            { "name": "Thesis & Argument", "score": 35, "maxScore": 40, "comments": "What was done well or badly." }
          ],
          "strengths": ["At most three points"],
          "improvements": ["At most three points"]
        }
        """;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions ParseOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly IReadOnlyList<string> EmptyList = [];

    // Less text than this is not worth sending, and would be graded as if it were
    private const int MinimumAssessmentCharacters = 200;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAiService _aiService;
    private readonly AiOptions _aiOptions;
    private readonly ILogger<GradingService> _logger;

    public GradingService(
        IUnitOfWork unitOfWork,
        IAiService aiService,
        IOptions<AiOptions> options,
        ILogger<GradingService> logger)
    {
        _unitOfWork = unitOfWork;
        _aiService = aiService;
        _aiOptions = options.Value;
        _logger = logger;
    }

    public async Task<GradingResult> GradeAsync(
        Guid submissionId,
        GradingRequestDto request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Tracked, the grade is written back onto this very row
        var submission = await _unitOfWork.Submissions
            .GetOwnedTrackedAsync(submissionId, userId, cancellationToken);

        if (submission is null)
        {
            return GradingResult.Failure(
                $"No submission with id {submissionId} was found.",
                StatusCodes.Status404NotFound);
        }

        var rubric = await FindRubricAsync(submission, cancellationToken);
        var document = await FindDocumentAsync(submission, cancellationToken);
        var rejection = DescribeWhatIsMissing(submission, rubric, document);

        // One check narrows both, they are known good once it passed
        if (rejection is not null || rubric is null || document is null)
        {
            return GradingResult.Failure(
                rejection ?? "This submission cannot be graded.",
                StatusCodes.Status422UnprocessableEntity);
        }

        var prompt = BuildPrompt(rubric, submission, document, request);

        if (prompt.Messages is null)
        {
            return GradingResult.Failure(prompt.Failure!, StatusCodes.Status422UnprocessableEntity);
        }

        // Set before the long call so a client asking how it is going sees this
        await SetStatusAsync(submission, GradeState.InProgress, string.Empty, cancellationToken);

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

            await SetStatusAsync(submission, GradeState.Failure, message, cancellationToken);

            _logger.LogWarning(
                "Grading submission {SubmissionId} failed with {StatusCode}: {Error}",
                submission.Id, chat.StatusCode, message);

            return GradingResult.Failure(
                message,
                chat.StatusCode ?? StatusCodes.Status502BadGateway);
        }

        var parsed = Parse(chat.Response.Content);

        if (parsed is null)
        {
            const string unreadable = "The model did not return a readable evaluation, nothing was stored.";

            await SetStatusAsync(submission, GradeState.Failure, unreadable, cancellationToken);

            return GradingResult.Failure(unreadable, StatusCodes.Status502BadGateway);
        }

        var evaluation = BuildEvaluation(submission, rubric, parsed, chat.Response, prompt.Truncated);

        submission.Evaluation = JsonSerializer.Serialize(evaluation, SerializerOptions);
        submission.GradingStatus = GradeState.Complete;
        submission.FailureReason = string.Empty;
        submission.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Graded submission {SubmissionId} against rubric {RubricId}: {Score} of {Max} ({Grade})",
            submission.Id, rubric.Id, evaluation.OverallScore, evaluation.MaxScore, evaluation.Grade);

        return GradingResult.Success(evaluation);
    }

    // The rubric the submission was filed with, null when it has none, when the
    // rubric is gone or when it has nothing in it to grade against
    private async Task<Rubric?> FindRubricAsync(Submission submission, CancellationToken cancellationToken)
    {
        if (submission.RubricId is null)
        {
            return null;
        }

        var rubric = await _unitOfWork.Rubrics.GetByIdAsync(submission.RubricId.Value, cancellationToken);

        return string.IsNullOrWhiteSpace(rubric?.RubricContent) ? null : rubric;
    }

    // The uploaded assessment behind the submission, null when there is none or
    // when nothing readable came out of it
    private async Task<Document?> FindDocumentAsync(Submission submission, CancellationToken cancellationToken)
    {
        if (submission.DocumentId is null)
        {
            return null;
        }

        var document = await _unitOfWork.Documents.GetByIdAsync(submission.DocumentId.Value, cancellationToken);

        return string.IsNullOrWhiteSpace(document?.ExtractedText) ? null : document;
    }

    // Says what exactly is missing, the caller turns it into a 422
    private static string? DescribeWhatIsMissing(Submission submission, Rubric? rubric, Document? document)
    {
        if (submission.RubricId is null)
        {
            return "This submission is not filed against a rubric, so there is nothing to grade it by.";
        }

        if (rubric is null)
        {
            return "The rubric this submission was filed with is gone or has no content to grade against.";
        }

        if (submission.DocumentId is null)
        {
            return "This submission is not filed against an uploaded document, so there is no assessment to grade.";
        }

        if (document is null)
        {
            return "The document this submission was filed with is gone, or nothing could be read out of it. Upload it again or reprocess it.";
        }

        if (document.ExtractedText!.Trim().Length < MinimumAssessmentCharacters)
        {
            return $"'{document.OriginalFileName}' holds too little text to grade.";
        }

        return null;
    }

    // The provider refuses an oversized prompt, so the assessment is cut to
    // whatever is left after the rubric and the instructions went in.
    private Prompt BuildPrompt(
        Rubric rubric,
        Submission submission,
        Document document,
        GradingRequestDto request)
    {
        var rubricSection =
            $"RUBRIC\nCourse: {rubric.CourseName} ({rubric.CourseId})\n{rubric.RubricContent.Trim()}";

        var assessmentHeader =
            $"ASSESSMENT\nStudent: {submission.StudentName} ({submission.StudentId})\n" +
            $"Source file: {document.OriginalFileName}\n";

        var instructions = string.IsNullOrWhiteSpace(request.Instructions)
            ? string.Empty
            : $"\nEXTRA INSTRUCTIONS FROM THE MARKER\n{request.Instructions.Trim()}\n";

        var budget = _aiOptions.MaxRequestCharacters -
            SystemPrompt.Length - rubricSection.Length - assessmentHeader.Length - instructions.Length;

        // Better to say so than to send an assessment that is only a few words
        if (budget < MinimumAssessmentCharacters)
        {
            return Prompt.Failed(
                $"The rubric of '{rubric.CourseName}' is too long to grade with, it leaves no room for the assessment.");
        }

        var assessment = document.ExtractedText!.Trim();
        var truncated = false;

        if (assessment.Length > budget)
        {
            assessment = assessment[..budget] + "\n\n[... the assessment was cut short, it did not fit ...]";
            truncated = true;
        }

        var user = $"{rubricSection}\n\n{assessmentHeader}{assessment}{instructions}";

        return Prompt.Ready(
            [new AiChatMessageDto("system", SystemPrompt), new AiChatMessageDto("user", user)],
            truncated);
    }

    // Reads the answer of the model. Models like to wrap the object in a
    // markdown fence or in a sentence, so the object is cut out before parsing.
    private static ModelEvaluation? Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var text = content.Trim();

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineBreak = text.IndexOf('\n');
            var closingFence = text.IndexOf("```", Math.Max(firstLineBreak, 0), StringComparison.Ordinal);

            text = firstLineBreak >= 0 && closingFence > firstLineBreak
                ? text[(firstLineBreak + 1)..closingFence]
                : text[(Math.Max(firstLineBreak, 0) + 1)..];
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        if (start >= 0 && end > start)
        {
            text = text[start..(end + 1)];
        }

        ModelEvaluation? evaluation;

        try
        {
            evaluation = JsonSerializer.Deserialize<ModelEvaluation>(text, ParseOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        // An object that carries nothing at all is as good as no answer
        if (evaluation is null ||
            (evaluation.OverallScore is null &&
             evaluation.Summary is null &&
             (evaluation.Criteria is null || evaluation.Criteria.Count == 0)))
        {
            return null;
        }

        return evaluation;
    }

    // Fills in what the model left out: totals it did not state are worked out
    // from the criteria and the percentage follows from the two of them
    private static EvaluationDto BuildEvaluation(
        Submission submission,
        Rubric rubric,
        ModelEvaluation parsed,
        AiChatResponseDto response,
        bool assessmentTruncated)
    {
        var criteria = (parsed.Criteria ?? [])
            .Where(criterion => !string.IsNullOrWhiteSpace(criterion.Name))
            .Select(criterion => new CriterionEvaluationDto(
                criterion.Name!.Trim(),
                criterion.Score,
                criterion.MaxScore,
                string.IsNullOrWhiteSpace(criterion.Comments) ? null : criterion.Comments.Trim()))
            .ToList();

        var maxScore = parsed.MaxScore;

        if (maxScore is null || maxScore <= 0)
        {
            var summed = criteria.Where(criterion => criterion.MaxScore is > 0).Sum(criterion => criterion.MaxScore!.Value);
            maxScore = summed > 0 ? summed : (double?)null;
        }

        var percentage = parsed.OverallScore is not null && maxScore is > 0
            ? Math.Round(parsed.OverallScore.Value / maxScore.Value * 100, 1)
            : (double?)null;

        return new EvaluationDto(
            submission.Id,
            submission.RubricId,
            submission.DocumentId,
            submission.StudentId,
            submission.StudentName,
            rubric.CourseName,
            rubric.CourseId,
            parsed.OverallScore,
            maxScore,
            percentage,
            string.IsNullOrWhiteSpace(parsed.Grade) ? null : parsed.Grade.Trim(),
            string.IsNullOrWhiteSpace(parsed.Summary) ? null : parsed.Summary.Trim(),
            criteria,
            Clean(parsed.Strengths),
            Clean(parsed.Improvements),
            assessmentTruncated || response.Truncated,
            response.Model,
            DateTime.UtcNow);
    }

    // Drops the blank entries a model sometimes leaves in a list
    private static IReadOnlyList<string> Clean(List<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return EmptyList;
        }

        var cleaned = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToList();

        return cleaned.Count == 0 ? EmptyList : cleaned;
    }

    private async Task SetStatusAsync(
        Submission submission,
        GradeState state,
        string reason,
        CancellationToken cancellationToken)
    {
        submission.GradingStatus = state;
        submission.FailureReason = reason;
        submission.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // Either the messages to send, or why they could not be built
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

    // The shape the model is asked for. Everything is optional because a model
    // leaves things out, the missing values are worked out afterwards.
    private sealed class ModelEvaluation
    {
        public double? OverallScore { get; set; }

        public double? MaxScore { get; set; }

        public string? Grade { get; set; }

        public string? Summary { get; set; }

        public List<ModelCriterion>? Criteria { get; set; }

        public List<string>? Strengths { get; set; }

        public List<string>? Improvements { get; set; }
    }

    private sealed class ModelCriterion
    {
        public string? Name { get; set; }

        public double? Score { get; set; }

        public double? MaxScore { get; set; }

        public string? Comments { get; set; }
    }
}
