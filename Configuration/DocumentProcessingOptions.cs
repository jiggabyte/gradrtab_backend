namespace GradrTab.Configuration;

// Bound from the "DocumentProcessing" section of appsettings.
public class DocumentProcessingOptions
{
    public const string SectionName = "DocumentProcessing";

    // Largest single file that will be accepted, in bytes
    public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024;

    // Folder (relative to the content root) in which uploaded files are kept
    public string StorageRoot { get; set; } = "Storage/Documents";

    // Upper bound on the number of structured entries stored per document
    public int MaxEntriesPerDocument { get; set; } = 2000;

    // Upper bound on the number of files accepted in a single multipart request
    public int MaxFilesPerRequest { get; set; } = 20;

    // Upper bound on the amount of extracted text kept for a single document
    public int MaxStoredTextCharacters { get; set; } = 1_000_000;

    // File extensions that may be uploaded, matched case insensitively
    public string[] AllowedExtensions { get; set; } =
    [
        ".pdf",
        ".docx", ".docm", ".doc",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff",
        ".csv", ".json", ".txt", ".md"
    ];

    public OcrOptions Ocr { get; set; } = new();
}

public class OcrOptions
{
    // Optical character recognition is used to read text out of images.
    // It requires the Tesseract native libraries and a tessdata folder to be present,
    // when they are missing images are still stored with their metadata.
    public bool Enabled { get; set; } = true;

    // Tesseract language codes, e.g. "eng" or "eng+fra"
    public string Languages { get; set; } = "eng";

    // Optional explicit path to the tessdata folder
    public string? TessDataPath { get; set; }
}