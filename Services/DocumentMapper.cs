using System.Text.Json;
using GradrTab.DTOs;
using GradrTab.Models;

namespace GradrTab.Services;

// Turns the persisted document entities into the API shapes.
public static class DocumentMapper
{
    private static readonly string[] EmptyWarnings = [];

    public static DocumentResponseDto ToResponse(this Document document) => new(
        document.Id,
        document.OriginalFileName,
        document.ContentType,
        document.FileExtension,
        document.FileSizeBytes,
        document.DocumentType.ToString(),
        document.Status.ToString(),
        document.CharacterCount,
        document.WordCount,
        document.PageCount,
        document.EntryCount,
        Deserialize<Dictionary<string, object?>>(document.Metadata),
        Deserialize<IReadOnlyList<string>>(document.Warnings) ?? EmptyWarnings,
        document.ErrorMessage,
        document.UploadedByUserId,
        document.CreatedAt,
        document.UpdatedAt,
        document.ProcessedAt);

    public static DocumentEntryResponseDto ToResponse(this DocumentEntry entry) => new(
        entry.Id,
        entry.EntryIndex,
        entry.EntryType,
        entry.Key,
        entry.Content,
        Deserialize<Dictionary<string, object?>>(entry.Data),
        entry.CreatedAt);

    // The stored jsonb columns are strings, malformed content is reported as missing
    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}