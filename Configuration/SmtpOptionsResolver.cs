namespace GradrTab.Configuration;

/// <summary>
/// Fills the Smtp section from friendlier environment variables, the same way
/// DbConnectionResolver accepts DB_* next to the standard config keys.
/// Priority: SMTP_* (environment) > Smtp__* (configuration) > appsettings.json.
/// The environment has to win, otherwise the built in defaults would mask a
/// missing setting: Host defaults to something non empty, so an appsettings
/// value (or that default) would always beat SMTP_HOST and the process would
/// quietly talk to localhost instead of the relay the dashboard configured.
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

    // The real environment variable wins over configuration. Values copied
    // straight out of appsettings.Example.json ("<add password here>") are
    // treated as not set, so a placeholder can never be sent as a credential.
    private static string First(string? configured, string variableName)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(variableName);

        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        return string.IsNullOrWhiteSpace(configured) || IsPlaceholder(configured) ? string.Empty : configured.Trim();
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

    // appsettings.Example.json ships the secrets as <add password here> and the
    // AI key as <api-key-here>. Anything in angle brackets is a template value.
    private static bool IsPlaceholder(string value) =>
        value.Trim().StartsWith('<') && value.Trim().EndsWith('>');
}
