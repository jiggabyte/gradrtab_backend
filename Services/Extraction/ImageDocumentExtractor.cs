using GradrTab.Services;
using SixLabors.ImageSharp;
using DocType = GradrTab.Models.DocumentType;

namespace GradrTab.Services.Extraction;

// Records information about an uploaded image and, when an OCR engine is
// available, the text recognised inside it.
public sealed class ImageDocumentExtractor : IDocumentExtractor
{
    private readonly IOcrService _ocrService;
    private readonly ILogger<ImageDocumentExtractor> _logger;

    public ImageDocumentExtractor(IOcrService ocrService, ILogger<ImageDocumentExtractor> logger)
    {
        _ocrService = ocrService;
        _logger = logger;
    }

    public DocType DocumentType => DocType.Image;

    public IReadOnlyCollection<string> SupportedExtensions { get; } =
        [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff"];

    public async Task<DocumentExtractionResult> ExtractAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var result = new DocumentExtractionResult();
        var bytes = await TextUtilities.ReadAllBytesAsync(content, cancellationToken);

        result.Metadata["fileSizeBytes"] = bytes.LongLength;

        if (bytes.Length > 0)
        {
            ReadImageInformation(result, bytes);
        }

        // Recognised text becomes the body of the document and is searchable
        if (_ocrService.IsAvailable && bytes.Length > 0)
        {
            try
            {
                var recognised = await _ocrService.ReadTextAsync(bytes, cancellationToken);

                if (recognised.Length > 0)
                {
                    result.Text = recognised;
                    result.Entries.Add(new ExtractedEntry(
                        0,
                        "OcrText",
                        "Recognised text",
                        recognised,
                        new Dictionary<string, object?>
                        {
                            ["wordCount"] = TextUtilities.CountWords(recognised),
                            ["engine"] = "tesseract"
                        }));
                }
                else
                {
                    result.Warnings.Add("Text recognition ran but found no readable text in this image.");
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Text recognition failed for {FileName}", fileName);
                result.Warnings.Add($"Text recognition failed: {exception.Message}");
            }
        }
        else if (!_ocrService.IsAvailable)
        {
            result.Warnings.Add($"Text recognition is unavailable, only image information was stored. {_ocrService.UnavailableReason}");
        }

        return result;
    }

    // Reads the dimensions, the format and any embedded Exif tags.
    private void ReadImageInformation(DocumentExtractionResult result, byte[] bytes)
    {
        try
        {
            using var image = Image.Load(bytes);

            result.Metadata["width"] = image.Width;
            result.Metadata["height"] = image.Height;
            result.Metadata["aspectRatio"] = Math.Round((double)image.Width / image.Height, 4);
            result.Metadata["megapixels"] = Math.Round((double)image.Width * image.Height / 1_000_000, 4);

            var format = image.Metadata.DecodedImageFormat;
            if (format is not null)
            {
                result.Metadata["format"] = format.Name;
                result.Metadata["mimeType"] = format.DefaultMimeType;
            }

            var exifProfile = image.Metadata.ExifProfile;
            if (exifProfile is null)
            {
                return;
            }

            foreach (var value in exifProfile.Values)
            {
                var underlying = value.GetValue();

                if (underlying is not null)
                {
                    result.Metadata[$"exif.{value.Tag}"] = underlying.ToString();
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogDebug(exception, "Could not decode the uploaded image header");
            result.Warnings.Add("The image header could not be decoded, the file itself was still stored.");
        }
    }
}