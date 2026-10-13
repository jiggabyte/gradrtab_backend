namespace GradrTab.Configuration;

// Resolves the AI provider key from the environment the same way
// SmtpOptionsResolver and DbConnectionResolver do.
// Priority: OPENROUTER_API_KEY (environment / .env / Render dashboard)
//         > Ai__ApiKey (standard .NET env binding, already in options.ApiKey)
//         > Ai:ApiKey from appsettings.json.
// Template values shipped in appsettings ("<add api key>", "<api-key-here>")
// are treated as not set, so a placeholder can never shadow the real key
// and can never be sent as a Bearer credential (OpenRouter answers that
// with 401 "Missing Authentication header" / invalid key).
public static class AiOptionsResolver
{
    public static void Apply(AiOptions options)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");

        if (!string.IsNullOrWhiteSpace(fromEnvironment) && !IsPlaceholder(fromEnvironment))
        {
            options.ApiKey = fromEnvironment.Trim();
            return;
        }

        // Ai__ApiKey env var is already bound into options.ApiKey by the
        // configuration provider, so just scrub placeholders here.
        if (string.IsNullOrWhiteSpace(options.ApiKey) || IsPlaceholder(options.ApiKey))
        {
            options.ApiKey = null;
        }
        else
        {
            options.ApiKey = options.ApiKey.Trim();
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            options.BaseUrl = "https://openrouter.ai/api/v1/chat/completions";
        }
        else
        {
            options.BaseUrl = options.BaseUrl.Trim();
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            options.Model = "cohere/north-mini-code:free";
        }
        else
        {
            options.Model = options.Model.Trim();
        }
    }

    // appsettings.Example.json ships secrets as <...> template values.
    // Anything in angle brackets is a template value, never a credential.
    internal static bool IsPlaceholder(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith('<') && trimmed.EndsWith('>');
    }
}
