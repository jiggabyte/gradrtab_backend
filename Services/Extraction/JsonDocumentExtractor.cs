using System.Text.Json;
using GradrTab.Configuration;
using Microsoft.Extensions.Options;
using DocType = GradrTab.Models.DocumentType;

namespace GradrTab.Services.Extraction;

// Reads JSON files. Arrays produce one entry per record, objects one entry per
// top level property, so the stored rows can be queried individually.
public sealed class JsonDocumentExtractor : IDocumentExtractor
{
    private static readonly JsonSerializerOptions PrettyPrintOptions = new() { WriteIndented = true };

    private readonly DocumentProcessingOptions _options;

    public JsonDocumentExtractor(IOptions<DocumentProcessingOptions> options)
    {
        _options = options.Value;
    }

    public DocType DocumentType => DocType.Json;

    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".json"];

    public async Task<DocumentExtractionResult> ExtractAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var result = new DocumentExtractionResult();

        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
        var root = document.RootElement;

        result.Text = JsonSerializer.Serialize(root, PrettyPrintOptions);
        result.Metadata["rootKind"] = root.ValueKind.ToString();

        var entryIndex = 0;
        var truncated = false;

        void AddEntry(string entryType, string? key, string? content, Dictionary<string, object?>? data)
        {
            if (entryIndex < _options.MaxEntriesPerDocument)
            {
                result.Entries.Add(new ExtractedEntry(entryIndex, entryType, key, content, data));
            }
            else
            {
                truncated = true;
            }

            entryIndex++;
        }

        switch (root.ValueKind)
        {
            case JsonValueKind.Array:
            {
                var itemCount = root.GetArrayLength();
                result.Metadata["itemCount"] = itemCount;

                foreach (var item in root.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    AddEntry(
                        "Record",
                        null,
                        IsScalar(item.ValueKind) ? item.ToString() : null,
                        ToDictionary(item));
                }

                break;
            }

            case JsonValueKind.Object:
            {
                var properties = root.EnumerateObject().ToArray();
                result.Metadata["propertyCount"] = properties.Length;

                foreach (var property in properties)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    AddEntry(
                        "Field",
                        property.Name,
                        IsScalar(property.Value.ValueKind) ? property.Value.ToString() : null,
                        new Dictionary<string, object?>
                        {
                            ["name"] = property.Name,
                            ["value"] = ToPlainObject(property.Value)
                        });
                }

                break;
            }

            default:
                AddEntry("Value", null, root.ToString(), null);
                break;
        }

        if (truncated)
        {
            result.Warnings.Add($"Only the first {_options.MaxEntriesPerDocument} JSON entries were stored.");
        }

        return result;
    }

    private static bool IsScalar(JsonValueKind kind) =>
        kind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null;

    private static Dictionary<string, object?>? ToDictionary(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var record = new Dictionary<string, object?>();

        foreach (var property in element.EnumerateObject())
        {
            record[property.Name] = ToPlainObject(property.Value);
        }

        return record;
    }

    private static object? ToPlainObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => ToDictionary(element),
        JsonValueKind.Array => element.EnumerateArray().Select(ToPlainObject).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetDecimal(out var number) ? number : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };
}