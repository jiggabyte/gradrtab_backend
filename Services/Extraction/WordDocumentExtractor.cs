using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GradrTab.Configuration;
using Microsoft.Extensions.Options;
using DocType = GradrTab.Models.DocumentType;

namespace GradrTab.Services.Extraction;

// Reads modern Word files (.docx/.docm) through the Open XML SDK and falls back
// to a best effort text scan for legacy binary .doc files.
public sealed class WordDocumentExtractor : IDocumentExtractor
{
    private const int MinimumLegacyRunLength = 6;

    // Names of the OLE compound file streams and of the built in Word styles.
    // They sit next to the real text in a binary .doc and would otherwise be
    // stored as if they were content.
    private static readonly HashSet<string> LegacyArtifactTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        // OLE compound document structure
        "Root Entry", "CompObj", "1Table", "0Table", "WordDocument", "Data",
        "SummaryInformation", "DocumentSummaryInformation", "ObjectPool",
        "Microsoft Word-Dokument", "Microsoft Word Document", "MSWordDoc",
        "Word.Document.8", "Word.Document.6", "Word.Document",
        "ThisDocument", "Module1", "VBA", "Macros",

        // Built in Word / LibreOffice style names
        "Normal", "Default Paragraph Font", "Normal Table", "No List",
        "Heading", "Heading 1", "Heading 2", "Heading 3", "Heading 4", "Heading 5", "Heading 6",
        "Body Text", "Caption", "Preformatted Text", "HTML Preformatted",
        "Header", "Footer", "Footnote Text", "Endnote Text", "Balloon Text",
        "Hyperlink", "FollowedHyperlink", "Table Grid", "Normal (Web)", "List Paragraph"
    };

    private readonly DocumentProcessingOptions _options;

    public WordDocumentExtractor(IOptions<DocumentProcessingOptions> options)
    {
        _options = options.Value;
    }

    public DocType DocumentType => DocType.Word;

    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".docx", ".docm", ".doc"];

    public async Task<DocumentExtractionResult> ExtractAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName);

        if (extension.Equals(".doc", StringComparison.OrdinalIgnoreCase))
        {
            return await ExtractLegacyDocAsync(content, cancellationToken);
        }

        return ExtractOpenXmlWord(content, cancellationToken);
    }

    private DocumentExtractionResult ExtractOpenXmlWord(Stream content, CancellationToken cancellationToken)
    {
        var result = new DocumentExtractionResult();
        var text = new StringBuilder();

        using var document = WordprocessingDocument.Open(content, false);

        var extendedProperties = document.ExtendedFilePropertiesPart?.Properties;
        AddIfPresent(result.Metadata, "application", extendedProperties?.Application?.Text);

        // Creator is a core (package level) property, not an extended one
        AddIfPresent(result.Metadata, "creator", document.PackageProperties?.Creator);

        AddIfPresent(result.Metadata, "company", extendedProperties?.Company?.Text);

        if (int.TryParse(extendedProperties?.Pages?.Text, out var pages))
        {
            result.PageCount = pages;
            result.Metadata["pageCount"] = pages;
        }

        if (int.TryParse(extendedProperties?.Words?.Text, out var words))
        {
            result.Metadata["reportedWordCount"] = words;
        }

        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            result.Warnings.Add("The Word document did not contain a readable body.");
            return result;
        }

        var entryIndex = 0;
        var truncated = false;

        void AddEntry(ExtractedEntry entry)
        {
            if (entryIndex < _options.MaxEntriesPerDocument)
            {
                result.Entries.Add(entry with { Index = entryIndex });
            }
            else
            {
                truncated = true;
            }

            entryIndex++;
        }

        foreach (var element in body.ChildElements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (element)
            {
                case Paragraph paragraph:
                {
                    var paragraphText = paragraph.InnerText?.Trim() ?? string.Empty;
                    text.AppendLine(paragraphText);

                    if (paragraphText.Length > 0)
                    {
                        AddEntry(new ExtractedEntry(entryIndex, "Paragraph", null, paragraphText, null));
                    }

                    break;
                }

                case Table table:
                {
                    foreach (var row in table.Elements<TableRow>())
                    {
                        var cells = row.Elements<TableCell>()
                            .Select(cell => cell.InnerText?.Trim() ?? string.Empty)
                            .ToArray();

                        var rowText = string.Join(" | ", cells);
                        text.AppendLine(rowText);

                        if (rowText.Length > 0)
                        {
                            AddEntry(new ExtractedEntry(
                                entryIndex,
                                "TableRow",
                                null,
                                rowText,
                                new Dictionary<string, object?> { ["cells"] = cells }));
                        }
                    }

                    break;
                }
            }
        }

        if (truncated)
        {
            result.Warnings.Add($"Only the first {_options.MaxEntriesPerDocument} paragraphs and table rows were stored as individual entries.");
        }

        result.Text = TextUtilities.Normalize(text.ToString());

        if (result.Text.Length == 0)
        {
            result.Warnings.Add("No text was found inside this Word document.");
        }

        return result;
    }

    private async Task<DocumentExtractionResult> ExtractLegacyDocAsync(Stream content, CancellationToken cancellationToken)
    {
        var result = new DocumentExtractionResult();
        var bytes = await TextUtilities.ReadAllBytesAsync(content, cancellationToken);

        result.Metadata["format"] = "Word 97-2003 (binary)";
        result.Warnings.Add("Legacy .doc files are read with a best effort text scan, layout and tables are not preserved. Convert the file to .docx for full extraction.");

        var runs = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Word 97-2003 keeps its text stream as UTF-16LE, some parts use code page bytes
        CollectReadableRuns(Encoding.Unicode.GetString(bytes), runs, seen);
        CollectReadableRuns(Encoding.Latin1.GetString(bytes), runs, seen);

        var text = TextUtilities.Normalize(string.Join("\n", runs));
        result.Text = text;

        if (text.Length == 0)
        {
            result.Warnings.Add("No readable text could be recovered from this .doc file.");
            return result;
        }

        // Every recovered line becomes its own searchable entry, like the plain text extractor does
        var entryIndex = 0;
        var truncated = false;

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                continue;
            }

            if (entryIndex < _options.MaxEntriesPerDocument)
            {
                result.Entries.Add(new ExtractedEntry(
                    entryIndex,
                    "Line",
                    $"Line {entryIndex + 1}",
                    trimmed,
                    new Dictionary<string, object?>
                    {
                        ["lineNumber"] = entryIndex + 1,
                        ["wordCount"] = TextUtilities.CountWords(trimmed)
                    }));
            }
            else
            {
                truncated = true;
            }

            entryIndex++;
        }

        if (truncated)
        {
            result.Warnings.Add($"Only the first {_options.MaxEntriesPerDocument} lines were stored as individual entries.");
        }

        return result;
    }

    // Pulls the printable runs out of a binary stream while skipping structural noise.
    private static void CollectReadableRuns(string decoded, List<string> runs, HashSet<string> seen)
    {
        var builder = new StringBuilder();

        void Flush()
        {
            var candidate = builder.ToString().Trim();

            if (candidate.Length >= MinimumLegacyRunLength &&
                IsMostlyReadable(candidate) &&
                HasCharacterVariety(candidate) &&
                !LegacyArtifactTokens.Contains(candidate) &&
                seen.Add(candidate))
            {
                runs.Add(candidate);
            }

            builder.Clear();
        }

        foreach (var character in decoded)
        {
            if (IsReadableCharacter(character))
            {
                builder.Append(character);
                continue;
            }

            Flush();
        }

        Flush();
    }

    private static bool IsReadableCharacter(char character) =>
        character is '\t' or '\n' or '\r' ||
        character is >= ' ' and <= '~' ||
        character is >= '\u00A0' and <= '\u024F';

    private static bool IsMostlyReadable(string value)
    {
        var readable = 0;

        foreach (var character in value)
        {
            if (character is >= ' ' and <= '~' || character is >= '\u00A0' and <= '\u024F')
            {
                readable++;
            }
        }

        return readable * 10 >= value.Length * 9;
    }

    // Binary filler such as the long 0xFF paddings found in compound documents
    // decodes into runs that are made of very few different characters, are
    // dominated by a single repeated one, or are mostly non ASCII noise. Real
    // text passes all three tests.
    private static bool HasCharacterVariety(string value)
    {
        if (value.Length <= 4)
        {
            return true;
        }

        var counts = new Dictionary<char, int>();
        var considered = 0;
        var asciiAlphanumeric = 0;
        var nonAscii = 0;

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            considered++;

            if (character > '\u007F')
            {
                nonAscii++;
            }
            else if (char.IsAsciiLetterOrDigit(character))
            {
                asciiAlphanumeric++;
            }

            counts[character] = counts.TryGetValue(character, out var existing) ? existing + 1 : 1;
        }

        if (considered < 3 || counts.Count < 3)
        {
            return false;
        }

        // A single character making up more than half of the run means padding
        if (counts.Values.Max() * 2 > considered)
        {
            return false;
        }

        if (nonAscii == 0)
        {
            return true;
        }

        // The byte paddings of a compound file decode into runs that are mostly
        // non ASCII characters, real non English text is not shaped like that
        return nonAscii * 2 <= considered && asciiAlphanumeric >= 4;
    }

    private static void AddIfPresent(Dictionary<string, object?> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value;
        }
    }
}