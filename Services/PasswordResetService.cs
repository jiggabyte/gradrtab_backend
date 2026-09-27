using System.Net;
using System.Security.Cryptography;
using System.Text;
using GradrTab.Configuration;
using GradrTab.Models;
using GradrTab.Repositories;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using BC = BCrypt.Net.BCrypt;

namespace GradrTab.Services;

// Runs the forgot password flow: hands out a random single use token, mails a
// link built around it and swaps the password for a new BCrypt hash when the
// token comes back.
public sealed class PasswordResetService : IPasswordResetService
{
    private const int TokenBytes = 32;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailSender _emailSender;
    private readonly PasswordResetOptions _options;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(
        IUnitOfWork unitOfWork,
        IEmailSender emailSender,
        IOptions<PasswordResetOptions> options,
        ILogger<PasswordResetService> logger)
    {
        _unitOfWork = unitOfWork;
        _emailSender = emailSender;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsAvailable => _emailSender.IsAvailable;

    public string? UnavailableReason => _emailSender.UnavailableReason;

    public async Task<PasswordResetRequestResult> RequestResetAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        // Without a usable SMTP setup no link can be sent, say so instead of
        // pretending the mail is on its way
        if (!_emailSender.IsAvailable)
        {
            return PasswordResetRequestResult.Unavailable(_emailSender.UnavailableReason ?? "Email is not available.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return PasswordResetRequestResult.Accepted(emailSent: false);
        }

        var normalized = email.Trim().ToLowerInvariant();
        var user = await _unitOfWork.Users.GetByEmailAsync(normalized, cancellationToken);

        // Same answer as a real send, the caller must not learn whether the
        // address is registered
        if (user is null)
        {
            _logger.LogInformation("Password reset requested for an unknown address");
            return PasswordResetRequestResult.Accepted(emailSent: false);
        }

        var windowStart = DateTime.UtcNow.AddHours(-1);
        var recent = await _unitOfWork.PasswordResetTokens.CountSinceAsync(user.Id, windowStart, cancellationToken);

        if (recent >= Math.Max(1, _options.MaxRequestsPerHour))
        {
            _logger.LogWarning("Password reset for {Email} was throttled, {Recent} requests in the last hour", normalized, recent);
            return PasswordResetRequestResult.Accepted(emailSent: false);
        }

        // Only the newest link stays usable
        await _unitOfWork.PasswordResetTokens.InvalidateForUserAsync(user.Id, cancellationToken);

        var token = GenerateToken();
        var lifetime = TimeSpan.FromMinutes(Math.Clamp(_options.TokenLifetimeMinutes, 1, 1440));

        await _unitOfWork.PasswordResetTokens.AddAsync(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = HashToken(token),
            ExpiresAt = DateTime.UtcNow.Add(lifetime)
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var link = BuildResetLink(token);
        var result = await _emailSender.SendAsync(
            user.Email,
            "Reset your GradrTab password",
            BuildHtmlBody(user, link, lifetime),
            BuildTextBody(user, link, lifetime),
            cancellationToken);

        if (!result.Sent)
        {
            // The link is useless without the mail, do not leave a live token behind
            await _unitOfWork.PasswordResetTokens.InvalidateForUserAsync(user.Id, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogWarning("Password reset mail for {Email} could not be sent: {Error}", normalized, result.ErrorMessage);

            // Still the accepted answer. A failure here is only known because the
            // address exists, so reporting it would turn this endpoint into a way
            // of finding out who has an account. The reason is in the log, the
            // caller only ever sees the same 202 as for an unknown address.
            return PasswordResetRequestResult.Accepted(emailSent: false);
        }

        _logger.LogInformation("Password reset link sent to {Email}, valid for {Minutes} minutes", normalized, _options.TokenLifetimeMinutes);

        return PasswordResetRequestResult.Accepted(emailSent: true);
    }

    public async Task<PasswordResetCompletionResult> CompleteResetAsync(
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(newPassword))
        {
            return Invalid();
        }

        var minimumLength = Math.Clamp(_options.MinimumPasswordLength, 1, 128);

        if (newPassword.Length < minimumLength)
        {
            return new PasswordResetCompletionResult(
                PasswordResetStatus.PasswordTooShort,
                $"The new password must be at least {minimumLength} characters long.");
        }

        var stored = await _unitOfWork.PasswordResetTokens
            .GetActiveByHashAsync(HashToken(token), cancellationToken);

        // Unknown, already used and expired all read the same from the outside
        if (stored is null)
        {
            return Invalid();
        }

        var user = await _unitOfWork.Users.GetByIdAsync(stored.UserId, cancellationToken);

        if (user is null)
        {
            return Invalid();
        }

        if (_options.RequireDifferentPassword && BC.Verify(newPassword, user.PasswordHash))
        {
            return new PasswordResetCompletionResult(
                PasswordResetStatus.PasswordUnchanged,
                "The new password must be different from the current one.");
        }

        user.PasswordHash = BC.HashPassword(newPassword);
        user.UpdatedAt = DateTime.UtcNow;

        // The link is burnt and every other outstanding link of this user dies
        // with it. Everything here is committed by the single save below.
        stored.UsedAt = DateTime.UtcNow;
        await _unitOfWork.PasswordResetTokens.InvalidateForUserAsync(user.Id, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Password reset completed for {Email}", user.Email);

        return new PasswordResetCompletionResult(PasswordResetStatus.Completed, null);
    }

    // One wording for every reason the token was refused
    private static PasswordResetCompletionResult Invalid() =>
        new(PasswordResetStatus.InvalidToken, "This reset link is invalid or has expired. Please request a new one.");

    // 32 random bytes, URL safe, so the link survives a mail client untouched
    private static string GenerateToken() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));

    // The database only ever holds the hash, a stolen row is of no use
    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private string BuildResetLink(string token) =>
        $"{_options.FrontendBaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(token)}";

    private static string BuildTextBody(User user, string link, TimeSpan lifetime) =>
        $"Hello {user.FirstName}," + Environment.NewLine + Environment.NewLine +
        "Somebody asked to reset the password of your GradrTab account. " +
        "Open the link below to choose a new password. If it was not you, ignore this email, " +
        "your current password keeps working." + Environment.NewLine + Environment.NewLine +
        link + Environment.NewLine + Environment.NewLine +
        $"The link works once and expires in {lifetime.TotalMinutes:0} minutes.";

    // First names come from the user, so they are encoded before they reach the markup
    private static string BuildHtmlBody(User user, string link, TimeSpan lifetime) =>
        $"""
        <!DOCTYPE html>
        <html lang="en">
          <body style="margin:0;padding:24px;background:#f4f5f7;font-family:Helvetica,Arial,sans-serif;color:#1f2933;">
            <div style="max-width:520px;margin:0 auto;background:#ffffff;border-radius:10px;padding:32px;">
              <h1 style="margin:0 0 16px;font-size:20px;">Reset your GradrTab password</h1>
              <p style="margin:0 0 16px;line-height:1.5;">Hello {WebUtility.HtmlEncode(user.FirstName)},</p>
              <p style="margin:0 0 16px;line-height:1.5;">
                Somebody asked to reset the password of your GradrTab account. Use the button below to
                choose a new password. If this was not you, ignore this email, your current password keeps working.
              </p>
              <p style="margin:0 0 24px;">
                <a href="{WebUtility.HtmlEncode(link)}"
                   style="background:#2563eb;color:#ffffff;padding:12px 20px;border-radius:8px;text-decoration:none;display:inline-block;">
                  Choose a new password
                </a>
              </p>
              <p style="margin:0 0 8px;font-size:13px;color:#616e7c;word-break:break-all;">{WebUtility.HtmlEncode(link)}</p>
              <p style="margin:0;font-size:13px;color:#616e7c;">
                This link works once and expires in {lifetime.TotalMinutes:0} minutes.
              </p>
            </div>
          </body>
        </html>
        """;
}

