namespace GradrTab.Configuration;

/// <summary>
/// Fills the Smtp section from friendlier environment variables, the same way
/// DbConnectionResolver accepts DB_* next to the standard config keys.
/// Priority: Smtp__Host (configuration) > SMTP_HOST (environment).
/// </summary>
public static class SmtpOptionsResolver
{
    public static void Apply(SmtpOptions options)
    {
        options.Host = First(options.Host, "SMTP_HOST");

        var port = FirstInt(options.Port, "SMTP_PORT");
        if (port is not null)
        {
            options.Port = port.Value;
        }

        options.Username = First(options.Username, "SMTP_USERNAME");
        options.Password = First(options.Password, "SMTP_PASSWORD");
        options.FromEmail = First(options.FromEmail, "SMTP_FROM_EMAIL");
        options.FromName = First(options.FromName, "SMTP_FROM_NAME");

        var enableSsl = FirstBool(options.EnableSsl, "SMTP_ENABLE_SSL");
        if (enableSsl is not null)
        {
            options.EnableSsl = enableSsl.Value;
        }
    }

    private static string First(string? configured, string variableName)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(variableName);

        return string.IsNullOrWhiteSpace(fromEnvironment) ? configured ?? string.Empty : fromEnvironment.Trim();
    }

    private static int? FirstInt(int configured, string variableName)
    {
        var raw = Environment.GetEnvironmentVariable(variableName);

        return int.TryParse(raw, out var parsed) ? parsed : configured;
    }

    private static bool? FirstBool(bool configured, string variableName)
    {
        var raw = Environment.GetEnvironmentVariable(variableName);

        return bool.TryParse(raw, out var parsed) ? parsed : configured;
    }
}
