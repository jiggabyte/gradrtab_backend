using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using GradrTab.Configuration;
using Microsoft.Extensions.Options;
using DocType = GradrTab.Models.DocumentType;

namespace GradrTab.Services.Extraction;

// Reads CSV files, one document entry per row, keeping the column names as keys.
public sealed class CsvDocumentExtractor : IDocumentExtractor
{
    private readonly DocumentProcessingOptions _options;

    public CsvDocumentExtractor(IOptions<DocumentProcessingOptions> options)
    {
        _options = options.Value;
    }

    public DocType DocumentType => DocType.Csv;

    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".csv"];

    public async Task<DocumentExtractionResult> ExtractAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var result = new DocumentExtractionResult();

        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            DetectDelimiter = true,
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            BadDataFound = null
        };

        using var reader = new StreamReader(content, Encoding.UTF8, true, 1024, true);
        using var csv = new CsvReader(reader, configuration);

        if (!await csv.ReadAsync())
        {
            result.Warnings.Add("The CSV file did not contain any rows.");
            return result;
        }

        csv.ReadHeader();

        var headers = NormaliseHeaders(csv.HeaderRecord, csv.Parser.Count);
        result.Metadata["columns"] = headers;
        result.Metadata["delimiter"] = csv.Parser.Delimiter;

        var text = new StringBuilder();
        text.AppendLine(string.Join('\t', headers));

        var entryIndex = 0;
        var rowCount = 0;
        var truncated = false;

        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowCount++;

            var record = new Dictionary<string, object?>(headers.Length);
            for (var column = 0; column < headers.Length; column++)
            {
                record[headers[column]] = column < csv.Parser.Count ? csv.GetField(column) : null;
            }

            var rowText = csv.Parser.RawRecord?.TrimEnd('\r', '\n') ?? string.Empty;
            text.AppendLine(rowText);

            if (entryIndex < _options.MaxEntriesPerDocument)
            {
                result.Entries.Add(new ExtractedEntry(entryIndex, "Row", null, rowText, record));
            }
            else
            {
                truncated = true;
            }

            entryIndex++;
        }

        result.Metadata["rowCount"] = rowCount;

        if (truncated)
        {
            result.Warnings.Add($"Only the first {_options.MaxEntriesPerDocument} rows were stored as individual entries.");
        }

        if (rowCount == 0)
        {
            result.Warnings.Add("The CSV file only contained a header row.");
        }

        result.Text = TextUtilities.Normalize(text.ToString());

        return result;
    }

    // Guarantees one unique, non empty name per column so the stored records stay usable.
    private static string[] NormaliseHeaders(string[]? headers, int fieldCount)
    {
        var count = Math.Max(fieldCount, headers?.Length ?? 0);
        var names = new string[count];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < count; index++)
        {
            var name = headers is not null && index < headers.Length ? headers[index]?.Trim() : null;

            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"Column{index + 1}";
            }

            var candidate = name;
            var suffix = 2;

            while (!used.Add(candidate))
            {
                candidate = $"{name}_{suffix++}";
            }

            names[index] = candidate;
        }

        return names;
    }
}