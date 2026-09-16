using GradrTab.Services.Extraction;
using GradrTab.Tests.Support;

namespace GradrTab.Tests;

public class TextExtractorTests
{
    [Fact]
    public async Task ExtractsLinesAndCountsWords()
    {
        var extractor = new TextDocumentExtractor();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello world\n\nsecond line"));

        var result = await extractor.ExtractAsync(stream, "notes.txt");

        Assert.Contains("hello world", result.Text);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("Line", result.Entries[0].EntryType);
    }

    [Fact]
    public void CountsWords()
    {
        Assert.Equal(0, TextUtilities.CountWords(null));
        Assert.Equal(2, TextUtilities.CountWords("hello  world"));
    }

    [Fact]
    public void NormalizesBlankLines()
    {
        var normalized = TextUtilities.Normalize("a\n\n\nb\r\n\r\nc");
        Assert.Equal("a\n\nb\n\nc", normalized);
    }
}
