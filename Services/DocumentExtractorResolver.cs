using GradrTab.Models;
using GradrTab.Services.Extraction;

namespace GradrTab.Services;

// Chooses the extractor that understands a given file name.
public interface IDocumentExtractorResolver
{
    // Every extension the pipeline can handle, used to validate uploads
    IReadOnlyList<string> SupportedExtensions { get; }

    IDocumentExtractor? Resolve(string fileName);

    // The kind of document a file name maps to, null when nothing matches
    DocumentType? ResolveType(string fileName);

    // Comma separated list of the supported extensions, used in error messages
    string DescribeSupportedExtensions();
}

public sealed class DocumentExtractorResolver : IDocumentExtractorResolver
{
    private readonly Dictionary<string, IDocumentExtractor> _extractorsByExtension;

    public DocumentExtractorResolver(IEnumerable<IDocumentExtractor> extractors)
    {
        _extractorsByExtension = new Dictionary<string, IDocumentExtractor>(StringComparer.OrdinalIgnoreCase);

        foreach (var extractor in extractors)
        {
            foreach (var extension in extractor.SupportedExtensions)
            {
                var normalised = NormaliseExtension(extension);

                // The first extractor registered for an extension wins, later ones are ignored
                _extractorsByExtension.TryAdd(normalised, extractor);
            }
        }

        SupportedExtensions = _extractorsByExtension.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<string> SupportedExtensions { get; }

    public IDocumentExtractor? Resolve(string fileName)
    {
        var extension = GetExtension(fileName);

        if (extension.Length == 0)
        {
            return null;
        }

        return _extractorsByExtension.TryGetValue(extension, out var extractor) ? extractor : null;
    }

    public DocumentType? ResolveType(string fileName) => Resolve(fileName)?.DocumentType;

    public string DescribeSupportedExtensions() => string.Join(", ", SupportedExtensions);

    private static string GetExtension(string fileName) => NormaliseExtension(Path.GetExtension(fileName));

    private static string NormaliseExtension(string extension) =>
        extension.StartsWith('.') ? extension.ToLowerInvariant() : $".{extension.ToLowerInvariant()}";
}