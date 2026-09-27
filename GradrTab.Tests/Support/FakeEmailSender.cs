using System.Text.RegularExpressions;
using GradrTab.Services;

namespace GradrTab.Tests.Support;

// Records what would have been mailed instead of talking to an SMTP server.
public sealed class FakeEmailSender : IEmailSender
{
    public bool IsAvailable { get; set; } = true;

    public string? UnavailableReason { get; set; }

    // Makes the next send look like a transport failure
    public bool FailNextSend { get; set; }

    public List<SentEmail> Sent { get; } = [];

    public SentEmail? LastEmail => Sent.Count > 0 ? Sent[^1] : null;

    public Task<EmailSendResult> SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return Task.FromResult(EmailSendResult.Failure(UnavailableReason ?? "Email is not available."));
        }

        Sent.Add(new SentEmail(toEmail, subject, htmlBody, textBody));

        return Task.FromResult(FailNextSend
            ? EmailSendResult.Failure("The SMTP server refused the message.")
            : EmailSendResult.Success());
    }
}

public sealed record SentEmail(string To, string Subject, string HtmlBody, string? TextBody)
{
    // The reset link is the only place the raw token ever exists
    public string? Token
    {
        get
        {
            var match = Regex.Match(HtmlBody, @"token=([A-Za-z0-9\-_]+)");
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
