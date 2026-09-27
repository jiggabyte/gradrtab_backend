using GradrTab.Configuration;
using GradrTab.Services;
using GradrTab.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GradrTab.Tests.Support;

public sealed class FakeFileStorageService : IFileStorageService
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _root;

    public FakeFileStorageService(DocumentProcessingOptions? options = null)
    {
        _root = Path.Combine(Path.GetTempPath(), "gradrtab-test-storage", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public string RootPath => _root;

    public readonly List<string> DeletedPaths = [];

    public Task<string> SaveAsync(Stream content, string storedFileName, CancellationToken cancellationToken = default)
    {
        var safeName = Path.GetFileName(storedFileName);
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        _files[safeName] = buffer.ToArray();
        File.WriteAllBytes(Path.Combine(_root, safeName), _files[safeName]);
        return Task.FromResult(safeName);
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var safeName = Path.GetFileName(storagePath);
        if (!_files.TryGetValue(safeName, out var bytes))
        {
            throw new FileNotFoundException("The stored copy of this document no longer exists.", safeName);
        }

        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        DeletedPaths.Add(storagePath);
        _files.Remove(Path.GetFileName(storagePath));
        return Task.CompletedTask;
    }

    public string GetFullPath(string storagePath) => Path.Combine(_root, Path.GetFileName(storagePath));

    public static IFileStorageService Create(DocumentProcessingOptions? options = null) =>
        new FakeFileStorageService(options);
}

public sealed class FakeExtractorResolver : IDocumentExtractorResolver
{
    private readonly GradrTab.Services.Extraction.IDocumentExtractor _extractor;

    public FakeExtractorResolver(GradrTab.Services.Extraction.IDocumentExtractor extractor)
    {
        _extractor = extractor;
        SupportedExtensions = extractor.SupportedExtensions.ToList();
    }

    public IReadOnlyList<string> SupportedExtensions { get; }

    public GradrTab.Services.Extraction.IDocumentExtractor? Resolve(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return SupportedExtensions.Contains(ext) ? _extractor : null;
    }

    public Models.DocumentType? ResolveType(string fileName) => Resolve(fileName)?.DocumentType;

    public string DescribeSupportedExtensions() => string.Join(", ", SupportedExtensions);
}
