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


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Get Connection String
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

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

// Add JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var key = Encoding.UTF8.GetBytes(jwtSettings["Secret"]!);

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

app.UseHttpsRedirection();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();
