using System.Net;
using System.Net.Mail;
using GradrTab.Configuration;
using Microsoft.Extensions.Options;

namespace GradrTab.Services;

// Sends through an SMTP relay using the SmtpClient from the base class library,
// so no extra NuGet package is needed. A host, port and sender address are
// required; a username is optional for relays that do not authenticate.
public sealed class SmtpEmailSender : IEmailSender
{
    // SMTPS, the port where TLS is negotiated before the SMTP greeting. The
    // SmtpClient in the base class library only speaks STARTTLS.
    private const int ImplicitTlsPort = 465;

    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsAvailable =>
        _options.Enabled &&
        !string.IsNullOrWhiteSpace(_options.Host) &&
        _options.Port is > 0 and <= 65535 &&
        // SmtpClient can only do STARTTLS, never the implicit TLS of port 465.
        // Silently ignoring the setting would fail later inside the handshake,
        // so a 465 is reported as a configuration problem instead.
        _options.Port != ImplicitTlsPort &&
        !string.IsNullOrWhiteSpace(_options.FromEmail);

    public string? UnavailableReason
    {
        get
        {
            if (!_options.Enabled)
            {
                return "Email is switched off (Smtp:Enabled is false).";
            }

            if (string.IsNullOrWhiteSpace(_options.Host))
            {
                return "No SMTP host is configured. Set Smtp:Host or the SMTP_HOST environment variable.";
            }

            if (_options.Port is <= 0 or > 65535)
            {
                return $"The configured SMTP port ({_options.Port}) is not a valid port number.";
            }

            if (_options.Port == ImplicitTlsPort)
            {
                return "The SMTP port 465 needs implicit TLS, which System.Net.Mail.SmtpClient does not support. Use port 587 (STARTTLS) instead.";
            }

            if (string.IsNullOrWhiteSpace(_options.FromEmail))
            {
                return "No sender address is configured. Set Smtp:FromEmail or SMTP_FROM_EMAIL.";
            }

            return null;
        }
    }

    public async Task<EmailSendResult> SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            _logger.LogWarning("Not sending \"{Subject}\" to {Recipient}: {Reason}", subject, toEmail, UnavailableReason);
            return EmailSendResult.Failure(UnavailableReason ?? "Email is not available.");
        }

        if (string.IsNullOrWhiteSpace(toEmail))
        {
            return EmailSendResult.Failure("No recipient address was supplied.");
        }

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(_options.FromEmail, _options.FromName),
                Subject = subject,
                Body = string.IsNullOrWhiteSpace(textBody) ? htmlBody : textBody,
                IsBodyHtml = string.IsNullOrWhiteSpace(textBody),
                BodyEncoding = System.Text.Encoding.UTF8,
                SubjectEncoding = System.Text.Encoding.UTF8
            };

            message.To.Add(new MailAddress(toEmail.Trim()));

            Console.WriteLine($"Sending email to {toEmail} via {_options.Host}:{_options.Port}, SSL={_options.EnableSsl}");

            using var client = new SmtpClient(_options.Host, _options.Port)
            {
               
                EnableSsl = _options.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = Math.Clamp(_options.TimeoutSeconds, 1, 300) * 1000
            };

            // An empty username means the relay does not authenticate
            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                client.UseDefaultCredentials = false;
                client.Credentials = new NetworkCredential(_options.Username, _options.Password ?? string.Empty);
            }

            await client.SendMailAsync(message, cancellationToken);

            _logger.LogInformation("Sent \"{Subject}\" to {Recipient} over {Host}:{Port}", subject, toEmail, _options.Host, _options.Port);

            return EmailSendResult.Success();
        }
        catch (OperationCanceledException)
        {
            return EmailSendResult.Failure($"The SMTP server did not answer within {_options.TimeoutSeconds} seconds.");
        }
        catch (SmtpFailedRecipientsException exception)
        {
            _logger.LogWarning(exception, "The SMTP server refused the recipient {Recipient}", toEmail);
            return EmailSendResult.Failure($"The SMTP server refused the recipient address ({exception.Message}).");
        }
        catch (SmtpException exception)
        {
            _logger.LogWarning(exception, "Sending to {Recipient} failed", toEmail);
            return EmailSendResult.Failure($"The SMTP server refused the message ({exception.Message}).");
        }
        catch (Exception exception)
        {
            // Covers auth failures, bad addresses and anything else the transport throws
            _logger.LogWarning(exception, "Sending to {Recipient} failed", toEmail);
            return EmailSendResult.Failure($"The email could not be sent ({exception.Message}).");
        }
    }
}
