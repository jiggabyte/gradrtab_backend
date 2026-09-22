using System.Text;

namespace GradrTab.Services.Extraction;

// Small helpers shared by the extractors.
public static class TextUtilities
{
    // Number of whitespace separated tokens in the supplied text.
    public static int CountWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    // Collapses the blank line runs that most document formats leave behind.
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var blankLinePending = false;

        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                blankLinePending = builder.Length > 0;
                continue;
            }

            if (blankLinePending)
            {
                builder.Append('\n');
                blankLinePending = false;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(line.TrimEnd());
        }

        return builder.ToString();
    }

    // Reads the whole stream into a byte array.
    public static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream is MemoryStream memoryStream)
        {
            return memoryStream.ToArray();
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}