namespace GradrTab.Configuration;

// Bound from the "Smtp" section of appsettings. Every value can also be set as
// an environment variable: Smtp__Host, Smtp__Username, Smtp__Password, ...
public class SmtpOptions
{
    public const string SectionName = "Smtp";

    // Turning this off keeps the application running, the reset endpoints then
    // report that email is unavailable instead of failing to send.
    public bool Enabled { get; set; } = true;

    public string Host { get; set; } = "localhost";

    // 587 is the STARTTLS submission port. SmtpClient cannot do the implicit
    // TLS of 465, so 587 is the only usable choice here.
    public int Port { get; set; } = 587;

    // STARTTLS on 587, set to false when talking to a local relay such as MailHog
    public bool EnableSsl { get; set; } = true;

    // Leave both empty for a relay that does not need authentication
    public string? Username { get; set; }

    public string? Password { get; set; }

    public string FromEmail { get; set; } = "no-reply@gradrtab.local";

    public string FromName { get; set; } = "GradrTab";

    // Upper bound on how long a single send may take
    public int TimeoutSeconds { get; set; } = 30;
}
