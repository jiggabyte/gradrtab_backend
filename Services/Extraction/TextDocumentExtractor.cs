using System.Text;
using DocType = GradrTab.Models.DocumentType;

namespace GradrTab.Services.Extraction;

// Reads plain text files and stores every line as a separate entry.
public sealed class TextDocumentExtractor : IDocumentExtractor
{
    public DocType DocumentType => DocType.Text;

    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".txt", ".md"];

    public async Task<DocumentExtractionResult> ExtractAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, true, 1024, true);
        var raw = await reader.ReadToEndAsync(cancellationToken);

        var result = new DocumentExtractionResult
        {
            Text = TextUtilities.Normalize(raw)
        };

        var lineIndex = 0;

        foreach (var line in result.Text.Split('\n'))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (line.Length == 0)
            {
                continue;
            }

            result.Entries.Add(new ExtractedEntry(
                lineIndex++,
                "Line",
                $"Line {lineIndex}",
                line,
                new Dictionary<string, object?>
                {
                    ["lineNumber"] = lineIndex,
                    ["wordCount"] = TextUtilities.CountWords(line)
                }));
        }

        return result;
    }
}