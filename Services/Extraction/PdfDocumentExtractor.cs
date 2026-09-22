using GradrTab.Configuration;
using GradrTab.Models;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace GradrTab.Services.Extraction;

// Reads the text layer of a PDF one page at a time.
public sealed class PdfDocumentExtractor : IDocumentExtractor
{
    private readonly DocumentProcessingOptions _options;

    public PdfDocumentExtractor(IOptions<DocumentProcessingOptions> options)
    {
        _options = options.Value;
    }

    public DocumentType DocumentType => DocumentType.Pdf;

    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".pdf"];

    public Task<DocumentExtractionResult> ExtractAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var result = new DocumentExtractionResult();

        using var pdf = PdfDocument.Open(content);

        result.PageCount = pdf.NumberOfPages;
        result.Metadata["pageCount"] = pdf.NumberOfPages;
        result.Metadata["isEncrypted"] = pdf.IsEncrypted;

        var information = pdf.Information;
        if (information is not null)
        {
            AddIfPresent(result.Metadata, "title", information.Title);
            AddIfPresent(result.Metadata, "author", information.Author);
            AddIfPresent(result.Metadata, "subject", information.Subject);
            AddIfPresent(result.Metadata, "keywords", information.Keywords);
            AddIfPresent(result.Metadata, "creator", information.Creator);
            AddIfPresent(result.Metadata, "producer", information.Producer);
        }

        var pageTexts = new List<string>(pdf.NumberOfPages);
        var entryIndex = 0;
        var truncated = false;

        foreach (var page in pdf.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageText = TextUtilities.Normalize(BuildPageText(page));

            pageTexts.Add(pageText);
            result.Metadata.TryAdd("firstPageWidth", (double)page.Width);
            result.Metadata.TryAdd("firstPageHeight", (double)page.Height);

            if (entryIndex < _options.MaxEntriesPerDocument)
            {
                result.Entries.Add(new ExtractedEntry(
                    entryIndex,
                    "Page",
                    $"Page {page.Number}",
                    pageText,
                    new Dictionary<string, object?>
                    {
                        ["pageNumber"] = page.Number,
                        ["wordCount"] = TextUtilities.CountWords(pageText)
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
            result.Warnings.Add($"Only the first {_options.MaxEntriesPerDocument} pages were stored as individual entries.");
        }

        result.Text = string.Join("\n\n", pageTexts.Where(p => p.Length > 0));

        if (string.IsNullOrWhiteSpace(result.Text))
        {
            result.Warnings.Add("No text layer was found in this PDF, it is most likely a scanned document.");
        }

        return Task.FromResult(result);
    }

    private static void AddIfPresent(Dictionary<string, object?> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value;
        }
    }

    // PdfPig's plain page text does not always keep the line breaks, so the
    // lines are rebuilt from the position of every word on the page. This
    // assumes a single reading column, which covers the usual documents.
    private static string BuildPageText(Page page)
    {
        var words = page.GetWords().ToList();

        if (words.Count == 0)
        {
            return string.Empty;
        }

        var ordered = words
            .OrderByDescending(word => word.BoundingBox.Bottom)
            .ThenBy(word => word.BoundingBox.Left)
            .ToList();

        // Words sitting on the same baseline belong to the same line
        var lineTolerance = Math.Max(1d, MedianWordHeight(ordered) * 0.5d);
        var lines = new List<List<Word>>();

        foreach (var word in ordered)
        {
            var currentLine = lines.Count > 0 ? lines[^1] : null;

            if (currentLine is not null &&
                Math.Abs(currentLine[0].BoundingBox.Bottom - word.BoundingBox.Bottom) <= lineTolerance)
            {
                currentLine.Add(word);
            }
            else
            {
                lines.Add([word]);
            }
        }

        var rebuilt = lines.Select(line => string.Join(' ', line
            .OrderBy(word => word.BoundingBox.Left)
            .Select(word => word.Text)));

        return string.Join("\n", rebuilt);
    }

    private static double MedianWordHeight(List<Word> words)
    {
        var heights = words
            .Select(word => word.BoundingBox.Height)
            .Where(height => height > 0)
            .OrderBy(height => height)
            .ToList();

        return heights.Count == 0 ? 1d : heights[heights.Count / 2];
    }
}