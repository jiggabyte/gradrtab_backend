using System.Security.Claims;
using GradrTab.Configuration;
using GradrTab.Controllers;
using GradrTab.DTOs;
using GradrTab.Models;
using GradrTab.Repositories;
using GradrTab.Services;
using GradrTab.Services.Extraction;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GradrTab.Tests.Support;

public static class TestHelpers
{
    // Comfortably over the minimum the grading service insists on
    private const string DefaultAssessment =
        "Rising prices squeeze the household budget long before the headline " +
        "inflation rate makes the news. This essay argues that the burden falls " +
        "unevenly, because rent and energy are fixed costs that cannot be traded " +
        "away. The evidence is drawn from the 2025 household expenditure survey " +
        "and from three regional case studies, which show the same pattern in " +
        "both urban and rural districts. Citations are given in the appendix.";

    public static DocumentProcessingOptions DefaultOptions() => new()
    {
        MaxFileSizeBytes = 10 * 1024 * 1024,
        MaxFilesPerRequest = 20,
        MaxEntriesPerDocument = 2000,
        MaxStoredTextCharacters = 1_000_000,
        StorageRoot = Path.Combine(Path.GetTempPath(), "gradrtab-tests"),
        AllowedExtensions = [".pdf", ".docx", ".doc", ".png", ".jpg", ".csv", ".json", ".txt", ".md"],
    };

    public static IOptions<DocumentProcessingOptions> OptionsWrapper(DocumentProcessingOptions? options = null) =>
        Options.Create(options ?? DefaultOptions());

    public static AiOptions DefaultAiOptions(string? apiKey = "test-key") => new()
    {
        ApiKey = apiKey,
        BaseUrl = "https://openrouter.ai/api/v1/chat/completions",
        Model = "openai/gpt-4o-mini",
        SiteUrl = "https://gradrtab.example",
        SiteName = "GradrTab",
        TimeoutSeconds = 5,
        MaxRequestCharacters = 1_000,
        DefaultMaxTokens = 100,
    };

    public static IOptions<AiOptions> AiOptionsWrapper(AiOptions? options = null) =>
        Options.Create(options ?? DefaultAiOptions());

    public static PasswordResetOptions DefaultPasswordResetOptions() => new()
    {
        TokenLifetimeMinutes = 30,
        MaxRequestsPerHour = 5,
        FrontendBaseUrl = "https://app.gradrtab.example",
        MinimumPasswordLength = 8,
        RequireDifferentPassword = true,
    };

    public static IPasswordResetService CreatePasswordResetService(
        InMemoryUnitOfWork unitOfWork,
        FakeEmailSender? emailSender = null,
        PasswordResetOptions? options = null) =>
        new PasswordResetService(
            unitOfWork,
            emailSender ?? new FakeEmailSender(),
            Options.Create(options ?? DefaultPasswordResetOptions()),
            NullLogger<PasswordResetService>.Instance);

    public static IRubricService CreateRubricService(InMemoryUnitOfWork unitOfWork) =>
        new RubricService(unitOfWork, NullLogger<RubricService>.Instance);

    public static ISubmissionService CreateSubmissionService(InMemoryUnitOfWork unitOfWork) =>
        new SubmissionService(unitOfWork, NullLogger<SubmissionService>.Instance);

    public static IGradingService CreateGradingService(
        InMemoryUnitOfWork unitOfWork,
        FakeAiService aiService,
        AiOptions? options = null) =>
        new GradingService(
            unitOfWork,
            aiService,
            Options.Create(options ?? DefaultGradingAiOptions()),
            NullLogger<GradingService>.Instance);

    // The shared Ai test default allows 1 000 characters, which the grading
    // prompt does not even fit into, so grading uses the limit appsettings ships
    public static AiOptions DefaultGradingAiOptions()
    {
        var options = DefaultAiOptions();
        options.MaxRequestCharacters = 100_000;
        return options;
    }

    // Mirrors the other controller tests: the controller reads the user id from
    // the claims, so a test can pretend to be signed in or not
    public static SubmissionsController CreateSubmissionsController(
        InMemoryUnitOfWork unitOfWork,
        FakeAiService? aiService = null,
        Guid? userId = null) =>
        TestHelpers.AttachUser(
            new SubmissionsController(
                CreateSubmissionService(unitOfWork),
                CreateGradingService(unitOfWork, aiService ?? FakeAiService.AnsweringWith("{}")),
                NullLogger<SubmissionsController>.Instance),
            userId);

    // Puts a name identifier claim on the controller context
    public static T AttachUser<T>(T controller, Guid? userId) where T : ControllerBase
    {
        var claims = new List<Claim>();
        if (userId is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
        }

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
            }
        };

        return controller;
    }

    // A rubric plus an uploaded assessment long enough to be graded, filed
    // together as one submission
    public static (Guid SubmissionId, Rubric Rubric, Submission Submission) SeedGradableSubmission(
        InMemoryUnitOfWork unitOfWork,
        Guid userId,
        string? assessmentText = null)
    {
        var rubric = new Rubric
        {
            CourseName = "Economics",
            CourseId = "ECON-101",
            RubricContent = "Criteria: Thesis 40, Evidence 40, Mechanics 20",
            UploadedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        unitOfWork.Rubrics.AddAsync(rubric).GetAwaiter().GetResult();

        var document = SeedDocument(unitOfWork, userId, text: assessmentText ?? DefaultAssessment);

        var submission = new Submission
        {
            RubricId = rubric.Id,
            DocumentId = document.Id,
            StudentId = "S-1001",
            StudentName = "Ada Lovelace",
            GradingStatus = GradeState.NotStarted,
            UploadedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        unitOfWork.Submissions.AddAsync(submission).GetAwaiter().GetResult();
        unitOfWork.SaveChangesAsync().GetAwaiter().GetResult();

        return (submission.Id, rubric, submission);
    }

    // The payload a client sends to create or to update a rubric
    public static RubricWriteDto SampleRubric() => new()
    {
        CourseName = "Economics",
        CourseId = "ECON-101",
        RubricContent =
            """
            {
              "criteria": [
                { "id": "thesis", "name": "Thesis & Argument", "points": 40 },
                { "id": "evidence", "name": "Evidence", "points": 40 },
                { "id": "mechanics", "name": "Grammar & Citations", "points": 20 }
              ]
            }
            """
    };

    // The payload a client sends to create or to update a submission
    public static SubmissionWriteDto SampleSubmission(Guid? rubricId = null, Guid? documentId = null) => new()
    {
        RubricId = rubricId,
        DocumentId = documentId,
        StudentId = "S-1001",
        StudentName = "Ada Lovelace",
    };

    public static User SeedUser(InMemoryUnitOfWork uow, string email = "ada@example.com", string password = "oldPassword123")
    {
        var user = new User
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = email.ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        uow.Users.AddAsync(user).GetAwaiter().GetResult();
        uow.SaveChangesAsync().GetAwaiter().GetResult();

        return user;
    }

    public static IAiService CreateAiService(StubHttpMessageHandler handler, AiOptions? options = null) =>
        new OpenRouterAiService(
            StubHttpMessageHandler.Factory(handler),
            AiOptionsWrapper(options),
            NullLogger<OpenRouterAiService>.Instance);

    // Mirrors DocumentsControllerTests: the controller reads the user id from the claims
    public static AiController CreateAiController(IAiService service, AiOptions? options = null, Guid? userId = null)
    {
        var controller = new AiController(service, AiOptionsWrapper(options), NullLogger<AiController>.Instance);

        var claims = new List<Claim>();
        if (userId is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
        }

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
            }
        };

        return controller;
    }

    public static IDocumentProcessingService CreateProcessingService(
        IUnitOfWork unitOfWork,
        IFileStorageService storage,
        IDocumentExtractorResolver resolver,
        DocumentProcessingOptions? options = null) =>
        new DocumentProcessingService(
            unitOfWork,
            storage,
            resolver,
            OptionsWrapper(options),
            NullLogger<DocumentProcessingService>.Instance);

    public static IDocumentExtractor TextExtractor() => new TextDocumentExtractor();

    public static FormFile FormFileFromText(string fileName, string content, string contentType = "text/plain")
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "files", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    public static Document SeedDocument(
        InMemoryUnitOfWork uow,
        Guid userId,
        string fileName = "notes.txt",
        DocumentStatus status = DocumentStatus.Completed,
        DocumentType type = DocumentType.Text,
        string? text = "hello world")
    {
        var document = new Document
        {
            OriginalFileName = fileName,
            StoredFileName = Guid.NewGuid() + ".txt",
            StoragePath = Guid.NewGuid() + ".txt",
            ContentType = "text/plain",
            FileExtension = ".txt",
            FileSizeBytes = 11,
            DocumentType = type,
            Status = status,
            ExtractedText = text,
            CharacterCount = text?.Length ?? 0,
            WordCount = 2,
            EntryCount = 1,
            UploadedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ProcessedAt = DateTime.UtcNow,
        };
        uow.Documents.AddAsync(document).GetAwaiter().GetResult();
        uow.DocumentEntries.AddAsync(new DocumentEntry
        {
            DocumentId = document.Id,
            EntryIndex = 0,
            EntryType = "Line",
            Key = "Line 1",
            Content = text,
        }).GetAwaiter().GetResult();
        uow.SaveChangesAsync().GetAwaiter().GetResult();
        return document;
    }
}
