namespace GradrTab.Models;

// The kind of document that was uploaded. Persisted as a string for readability.
public enum DocumentType
{
    Pdf,
    Word,
    Image,
    Csv,
    Json,
    Text,
    Unknown
}