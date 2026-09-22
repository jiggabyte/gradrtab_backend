namespace GradrTab.Services;

// Reads text out of image files. Implementations must never throw when the
// underlying engine is missing, they report IsAvailable = false instead.
public interface IOcrService
{
    bool IsAvailable { get; }

    // Human readable explanation of why IsAvailable is false
    string? UnavailableReason { get; }

    Task<string> ReadTextAsync(byte[] imageBytes, CancellationToken cancellationToken = default);
}