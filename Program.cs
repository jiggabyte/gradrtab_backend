using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IdentityModel.Tokens;
using GradrTab.Configuration;
using GradrTab.Data;
using GradrTab.Repositories;
using GradrTab.Services;
using GradrTab.Services.Extraction;


// Load .env (local dev / Docker) into process env BEFORE the builder reads config.
// Real environment variables (e.g. Render dashboard) always win over .env values.
DotEnv.Load();

var builder = WebApplication.CreateBuilder(args);

// Render (and most PaaS) injects PORT. Bind Kestrel to 0.0.0.0:$PORT so the
// container is reachable. Falls back to launchSettings / 5120 locally.
var port = Environment.GetEnvironmentVariable("PORT") ?? "10000";
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://*:{port.Trim()}");
}

// Add services to the container.

// Resolve the Postgres connection string.
// Priority: DATABASE_URL (Render) > discrete DB_* vars > ConnectionStrings__DefaultConnection > appsettings.
var connectionString = DbConnectionResolver.Resolve(builder.Configuration);

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "No database connection string found. Set DATABASE_URL, DB_HOST/DB_NAME/DB_USER/DB_PASSWORD, " +
        "ConnectionStrings__DefaultConnection, or ConnectionStrings:DefaultConnection in appsettings.json.");
}

// Register DbContext to use PostgreSQL
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Document upload settings (size limits, allowed extensions, OCR)
builder.Services.Configure<DocumentProcessingOptions>(
    builder.Configuration.GetSection(DocumentProcessingOptions.SectionName));

var documentProcessingOptions = builder.Configuration
    .GetSection(DocumentProcessingOptions.SectionName)
    .Get<DocumentProcessingOptions>() ?? new DocumentProcessingOptions();

// Make sure Kestrel and the form reader accept multipart uploads up to the configured limit
var maxRequestBytes = documentProcessingOptions.MaxFileSizeBytes *
                      Math.Max(1, documentProcessingOptions.MaxFilesPerRequest);

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxRequestBytes;
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = 64 * 1024;
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxRequestBytes;
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Add JWT Authentication (secret can come from appsettings, .env, or JwtSettings__Secret env var)
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var jwtSecret = jwtSettings["Secret"] ?? Environment.GetEnvironmentVariable("JWT_SECRET");
if (string.IsNullOrWhiteSpace(jwtSecret))
{
    throw new InvalidOperationException(
        "Missing JWT secret. Set JwtSettings:Secret in appsettings or JWT_SECRET / JwtSettings__Secret env var.");
}
var key = Encoding.UTF8.GetBytes(jwtSecret);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ClockSkew = TimeSpan.Zero
    };
});

// 1. Add Native .NET 10 OpenAPI support
builder.Services.AddOpenApi();

builder.Services.AddControllers();

// Document ingestion: file storage, optional OCR and one extractor per format
builder.Services.AddSingleton<IFileStorageService, FileStorageService>();
builder.Services.AddSingleton<IOcrService, TesseractOcrService>();
builder.Services.AddSingleton<IDocumentExtractorResolver, DocumentExtractorResolver>();

builder.Services.AddSingleton<IDocumentExtractor, PdfDocumentExtractor>();
builder.Services.AddSingleton<IDocumentExtractor, WordDocumentExtractor>();
builder.Services.AddSingleton<IDocumentExtractor, CsvDocumentExtractor>();
builder.Services.AddSingleton<IDocumentExtractor, JsonDocumentExtractor>();
builder.Services.AddSingleton<IDocumentExtractor, ImageDocumentExtractor>();
builder.Services.AddSingleton<IDocumentExtractor, TextDocumentExtractor>();

builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IDocumentProcessingService, DocumentProcessingService>();

var app = builder.Build();

app.UseForwardedHeaders();

// Apply EF Core migrations at startup so Render/Docker deploys don't need a separate migrate step.
using (var scope = app.Services.CreateScope())
{
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
        startupLogger.LogInformation("Database migration completed successfully.");
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "Database migration failed.");
        throw;
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // Generates the raw /openapi/v1.json spec file
    app.MapOpenApi();

    // Attaches Swagger UI and points it directly to the native v1.json path
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "GradrTab API v1");
    });
}

// Behind Render's TLS-terminating proxy, redirect only when the original
// request was plain HTTP; locally keep the old behaviour in Development.
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
else if (string.Equals(Environment.GetEnvironmentVariable("HTTPS_REDIRECT"), "true", StringComparison.OrdinalIgnoreCase))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();

app.UseAuthorization();

// Render health check / Docker HEALTHCHECK target (no auth).
app.MapGet("/health", () => Results.Ok(new { status = "healthy", time = DateTime.UtcNow }));

app.MapControllers();

app.Run();
