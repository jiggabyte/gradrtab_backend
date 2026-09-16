using GradrTab.Services.Extraction;
using GradrTab.Tests.Support;
using Microsoft.Extensions.Options;

namespace GradrTab.Tests;

public class CsvExtractorTests
{
    [Fact]
    public async Task ExtractsRowsWithHeaders()
    {
        var extractor = new CsvDocumentExtractor(TestHelpers.OptionsWrapper());
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("name,age\nAda,36\nGrace,85"));

        var result = await extractor.ExtractAsync(stream, "people.csv");

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("Row", result.Entries[0].EntryType);
        Assert.Equal(2, (int?)result.Metadata["rowCount"] ?? (int)(long)result.Metadata["rowCount"]!);
    }
}

public class JsonExtractorTests
{
    [Fact]
    public async Task ExtractsArrayRecords()
    {
        var extractor = new JsonDocumentExtractor(TestHelpers.OptionsWrapper());
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("[{\"a\":1},{\"a\":2}]"));

        var result = await extractor.ExtractAsync(stream, "data.json");

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("Record", result.Entries[0].EntryType);
    }
}

public class ExtractorResolverTests
{
    [Fact]
    public void ResolvesTextExtractor()
    {
        var resolver = new GradrTab.Services.DocumentExtractorResolver(
            new IDocumentExtractor[] { new TextDocumentExtractor() });

        Assert.NotNull(resolver.Resolve("notes.txt"));
        Assert.Null(resolver.Resolve("archive.zip"));
        Assert.Equal(GradrTab.Models.DocumentType.Text, resolver.ResolveType("notes.md"));
        Assert.Contains(".txt", resolver.DescribeSupportedExtensions());
    }
}
