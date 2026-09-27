using System.Net.Mail;

namespace GradrTab.Services;

// Sends transactional email. Implementations must never throw when SMTP is not
// configured, they report IsAvailable = false and a reason instead, so the
// application still starts and the rest of the API keeps working.
public interface IEmailSender
{
    bool IsAvailable { get; }

    // Human readable explanation of why IsAvailable is false
    string? UnavailableReason { get; }

    Task<EmailSendResult> SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default);
}

// Outcome of one send, a transport failure is reported instead of thrown
public sealed record EmailSendResult(bool Sent, string? ErrorMessage)
{
    public static EmailSendResult Success() => new(true, null);

    public static EmailSendResult Failure(string message) => new(false, message);
}
