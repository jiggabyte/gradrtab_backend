namespace GradrTab.Models;

// Lifecycle of an uploaded document as it moves through the extraction pipeline.
public enum DocumentStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}