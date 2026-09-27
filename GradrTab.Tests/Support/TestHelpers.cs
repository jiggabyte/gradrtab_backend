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

    public static RubricOptions DefaultRubricOptions() => new()
    {
        MaxCriteriaPerRubric = 50,
        MaxLevelsPerCriterion = 10,
    };

    public static IRubricService CreateRubricService(
        InMemoryUnitOfWork unitOfWork,
        RubricOptions? options = null) =>
        new RubricService(
            unitOfWork,
            Options.Create(options ?? DefaultRubricOptions()),
            NullLogger<RubricService>.Instance);

    // The payload the frontend submits, matching samples/rubric_schema.json
    public static RubricSubmissionDto SampleRubric() => new()
    {
        Id = "rub_essay",
        Title = "Research Essay",
        Criteria =
        [
            new RubricCriterionDto
            {
                Id = "thesis",
                Name = "Thesis & Argument",
                Levels =
                [
                    new RubricLevelDto { Label = "Strong", Points = 40, Description = "Clear, arguable thesis sustained throughout." },
                    new RubricLevelDto { Label = "Adequate", Points = 25, Description = "Thesis present but unevenly supported." },
                    new RubricLevelDto { Label = "Weak", Points = 10, Description = "No clear thesis." }
                ]
            },
            new RubricCriterionDto
            {
                Id = "evidence",
                Name = "Evidence",
                Levels =
                [
                    new RubricLevelDto { Label = "Strong", Points = 40, Description = "Credible sources, analysed rather than just quoted." },
                    new RubricLevelDto { Label = "Adequate", Points = 25, Description = "Relevant sources, little analysis." },
                    new RubricLevelDto { Label = "Weak", Points = 10, Description = "Little or no evidence." }
                ]
            },
            new RubricCriterionDto
            {
                Id = "mechanics",
                Name = "Grammar & Citations",
                Levels =
                [
                    new RubricLevelDto { Label = "Strong", Points = 20, Description = "Error-free; citations correct." },
                    new RubricLevelDto { Label = "Adequate", Points = 12, Description = "Minor errors; citations mostly correct." },
                    new RubricLevelDto { Label = "Weak", Points = 5, Description = "Frequent errors; citations missing." }
                ]
            }
        ]
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
