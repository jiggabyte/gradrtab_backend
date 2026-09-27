namespace GradrTab.Configuration;

// Bound from the "Ai" section of appsettings. Every value can also come from an
// environment variable: Ai__Model, Ai__ApiKey, ... plus the OPENROUTER_API_KEY
// fallback for the key itself.
public class AiOptions
{
    public const string SectionName = "Ai";

    // OpenRouter API key. Leave empty in appsettings and set OPENROUTER_API_KEY
    // (or Ai__ApiKey) in the environment / Render dashboard instead.
    public string? ApiKey { get; set; }

    // Endpoint the chat completion request is posted to
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1/chat/completions";

    // Model used when a request does not name one
    public string Model { get; set; } = "cohere/north-mini-code:free";

    // Optional ranking headers, both are sent only when they are set
    public string? SiteUrl { get; set; }

    public string? SiteName { get; set; }

    // Upper bound on how long a single completion may take
    public int TimeoutSeconds { get; set; } = 120;

    // Upper bound on the combined length of all messages in one request
    public int MaxRequestCharacters { get; set; } = 100_000;

    // Upper bound on the completion length requested from the provider
    public int DefaultMaxTokens { get; set; } = 2_000;
}
