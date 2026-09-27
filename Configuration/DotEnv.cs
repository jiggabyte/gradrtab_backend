namespace GradrTab.Configuration;

/// <summary>
/// Minimal .env loader (no extra NuGet dependency).
/// Reads a `.env` file in the content root / working directory and sets
/// any keys that are not already defined in the process environment.
/// Supports: KEY=value, KEY="quoted value", export KEY=value, # comments.
/// Real environment variables (e.g. Render dashboard) always win over .env.
/// </summary>
public static class DotEnv
{
    public static void Load(string? filePath = null)
    {
        string? path = filePath;

        if (path is null)
        {
            var baseDirEnv = Path.Combine(AppContext.BaseDirectory, ".env");
            var cwdEnv = Path.Combine(Directory.GetCurrentDirectory(), ".env");
            if (File.Exists(baseDirEnv))
            {
                path = baseDirEnv;
            }
            else if (File.Exists(cwdEnv))
            {
                path = cwdEnv;
            }
        }

        if (path is null || !File.Exists(path))
        {
            return;
        }

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
            {
                line = line["export ".Length..].TrimStart();
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }
            else
            {
                var commentIndex = value.IndexOf(" #", StringComparison.Ordinal);
                if (commentIndex >= 0)
                {
                    value = value[..commentIndex].TrimEnd();
                }
            }

            // Never overwrite real environment variables (Render dashboard wins).
            if (Environment.GetEnvironmentVariable(name) is null)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
