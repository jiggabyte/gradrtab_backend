using GradrTab.Configuration;
using Microsoft.Extensions.Options;

namespace GradrTab.Services;

// Keeps the uploaded originals on disk so a document can be re-processed later.
public interface IFileStorageService
{
    // Absolute path of the folder the uploads live in
    string RootPath { get; }

    // Stores the stream and returns the path relative to the storage root
    Task<string> SaveAsync(Stream content, string storedFileName, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default);

    Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default);

    string GetFullPath(string storagePath);
}

public sealed class FileStorageService : IFileStorageService
{
    private readonly ILogger<FileStorageService> _logger;

    public FileStorageService(
        IWebHostEnvironment environment,
        IOptions<DocumentProcessingOptions> options,
        ILogger<FileStorageService> logger)
    {
        var configuredRoot = options.Value.StorageRoot;

        RootPath = Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(environment.ContentRootPath, configuredRoot);

        Directory.CreateDirectory(RootPath);

        _logger = logger;
    }

    public string RootPath { get; }

    public async Task<string> SaveAsync(Stream content, string storedFileName, CancellationToken cancellationToken = default)
    {
        // Only ever the bare file name is used, so a caller cannot escape the storage root
        var safeName = Path.GetFileName(storedFileName);
        var fullPath = Path.Combine(RootPath, safeName);

        await using (var target = File.Create(fullPath))
        {
            await content.CopyToAsync(target, cancellationToken);
        }

        _logger.LogDebug("Stored upload at {Path}", fullPath);

        return safeName;
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(storagePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The stored copy of this document no longer exists.", fullPath);
        }

        return Task.FromResult<Stream>(File.OpenRead(fullPath));
    }

    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(storagePath);

        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception exception)
        {
            // A leftover file is not worth failing the request for
            _logger.LogWarning(exception, "Could not delete the stored upload at {Path}", fullPath);
        }

        return Task.CompletedTask;
    }

    public string GetFullPath(string storagePath) => Path.Combine(RootPath, Path.GetFileName(storagePath));
}