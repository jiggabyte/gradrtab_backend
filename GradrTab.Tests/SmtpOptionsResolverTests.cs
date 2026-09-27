using GradrTab.Configuration;

namespace GradrTab.Tests;

public class SmtpOptionsResolverTests
{
    // The resolver reads process wide environment variables, so every test sets
    // what it needs and clears it again to stay independent of the others.
    private static SmtpOptions Resolve(SmtpOptions options, params (string Key, string? Value)[] environment)
    {
        var previous = environment.ToDictionary(pair => pair.Key, pair => Environment.GetEnvironmentVariable(pair.Key));

        try
        {
            foreach (var (key, value) in environment)
            {
                Environment.SetEnvironmentVariable(key, value);
            }

            SmtpOptionsResolver.Apply(options);

            return options;
        }
        finally
        {
            foreach (var (key, value) in previous)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    [Fact]
    public void Apply_LetsTheEnvironmentOverrideTheConfiguredHost()
    {
        // appsettings.json is gitignored, so in the container Host keeps its
        // default. The dashboard value has to win over that default, otherwise
        // the app talks to localhost and the connection is refused.
        var options = Resolve(
            new SmtpOptions { Host = "localhost" },
            ("SMTP_HOST", "smtp.mail.yahoo.com"));

        Assert.Equal("smtp.mail.yahoo.com", options.Host);
    }

    [Fact]
    public void Apply_KeepsTheConfiguredHostWhenNoEnvironmentVariableIsSet()
    {
        var options = Resolve(
            new SmtpOptions { Host = "smtp.example.com" },
            ("SMTP_HOST", null));

        Assert.Equal("smtp.example.com", options.Host);
    }

    [Fact]
    public void Apply_DoesNotSendAPlaceholderAsACredential()
    {
        // appsettings.Example.json ships "<add password here>". Treating that as
        // a real password would authenticate with a literal template value.
        var options = Resolve(
            new SmtpOptions { Password = "<add password here>" },
            ("SMTP_PASSWORD", null));

        Assert.Equal(string.Empty, options.Password);
    }

    [Fact]
    public void Apply_TakesThePortAndTheTlsFlagFromTheEnvironment()
    {
        var options = Resolve(
            new SmtpOptions { Port = 465, EnableSsl = true },
            ("SMTP_PORT", "587"),
            ("SMTP_ENABLE_SSL", "false"));

        Assert.Equal(587, options.Port);
        Assert.False(options.EnableSsl);
    }

    [Fact]
    public void Apply_TrimsTheEnvironmentValues()
    {
        var options = Resolve(
            new SmtpOptions(),
            ("SMTP_FROM_EMAIL", "  no-reply@example.com  "));

        Assert.Equal("no-reply@example.com", options.FromEmail);
    }
}
