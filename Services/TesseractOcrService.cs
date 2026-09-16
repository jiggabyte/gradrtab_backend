using GradrTab.Configuration;
using GradrTab.Services.Extraction;
using Microsoft.Extensions.Options;
using Tesseract;

namespace GradrTab.Services;

// Wraps the Tesseract engine. The NuGet package ships the Windows native
// libraries, on other platforms libtesseract and a tessdata folder must be
// installed. Probing is lazy so the application still starts when OCR cannot
// run, images are then stored with their metadata only.
public sealed class TesseractOcrService : IOcrService, IDisposable
{
    private readonly OcrOptions _options;
    private readonly ILogger<TesseractOcrService> _logger;
    private readonly Lock _probeLock = new();
    private readonly SemaphoreSlim _engineLock = new(1, 1);

    private TesseractEngine? _engine;
    private bool _probed;
    private string? _unavailableReason;
    private bool _disposed;

    public TesseractOcrService(IOptions<DocumentProcessingOptions> options, ILogger<TesseractOcrService> logger)
    {
        _options = options.Value.Ocr;
        _logger = logger;
    }

    public bool IsAvailable
    {
        get
        {
            EnsureProbed();
            return _engine is not null;
        }
    }

    public string? UnavailableReason
    {
        get
        {
            EnsureProbed();
            return _unavailableReason;
        }
    }

    public async Task<string> ReadTextAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        EnsureProbed();

        var engine = _engine ?? throw new InvalidOperationException(_unavailableReason ?? "The OCR engine is not available.");

        // A TesseractEngine instance is not thread safe, so one image is processed at a time
        await _engineLock.WaitAsync(cancellationToken);
        try
        {
            using var pix = Pix.LoadFromMemory(imageBytes);
            using var page = engine.Process(pix);
            return TextUtilities.Normalize(page.GetText());
        }
        finally
        {
            _engineLock.Release();
        }
    }

    private void EnsureProbed()
    {
        if (_probed)
        {
            return;
        }

        lock (_probeLock)
        {
            if (_probed)
            {
                return;
            }

            _probed = true;

            if (!_options.Enabled)
            {
                _unavailableReason = "Text recognition is switched off (DocumentProcessing:Ocr:Enabled is false).";
                return;
            }

            try
            {
                var tessDataPath = ResolveTessDataPath();
                if (tessDataPath is null)
                {
                    _unavailableReason = "No Tesseract tessdata folder could be found. Install Tesseract (the native library plus a tessdata folder containing eng.traineddata), or point DocumentProcessing:Ocr:TessDataPath at an existing tessdata folder. Note that the Tesseract NuGet package only ships the Windows native libraries.";
                    return;
                }

                _engine = new TesseractEngine(tessDataPath, _options.Languages, EngineMode.Default);
                _logger.LogInformation("Tesseract OCR initialised with tessdata at {TessDataPath}", tessDataPath);
            }
            catch (Exception exception)
            {
                _unavailableReason = $"The Tesseract engine could not be started ({exception.Message}). Make sure the Tesseract native library for this platform is installed next to the application, or install it system wide.";
                _logger.LogWarning(exception, "Tesseract OCR is unavailable, images will be stored without recognised text");
            }
        }
    }

    private string? ResolveTessDataPath()
    {
        var candidates = new List<string?>
        {
            _options.TessDataPath,
            Environment.GetEnvironmentVariable("TESSDATA_PREFIX"),
            Path.Combine(AppContext.BaseDirectory, "tessdata"),
            Path.Combine(Directory.GetCurrentDirectory(), "tessdata"),
            "/usr/share/tesseract-ocr/5/tessdata",
            "/usr/share/tesseract-ocr/4.00/tessdata",
            "/usr/share/tessdata",
            "/usr/local/share/tessdata"
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "*.traineddata").Any())
                {
                    return candidate;
                }
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Ignoring unreadable tessdata candidate {Candidate}", candidate);
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _engine?.Dispose();
        _engine = null;
        _engineLock.Dispose();
    }
}