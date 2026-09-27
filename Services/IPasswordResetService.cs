namespace GradrTab.Services;

// Issues and redeems the single use tokens behind the forgot password flow.
public interface IPasswordResetService
{
    // True when a link could actually be mailed out
    bool IsAvailable { get; }

    string? UnavailableReason { get; }

    // Mails a reset link. The answer is deliberately the same for a known and
    // an unknown address so the endpoint cannot be used to find accounts.
    Task<PasswordResetRequestResult> RequestResetAsync(string email, CancellationToken cancellationToken = default);

    // Validates the token, sets the new password and burns every token of that user
    Task<PasswordResetCompletionResult> CompleteResetAsync(
        string token,
        string newPassword,
        CancellationToken cancellationToken = default);
}

public sealed record PasswordResetRequestResult(bool Succeeded, bool EmailSent, string? ErrorMessage, int StatusCode)
{
    // Also returned for an unknown address and for a throttled account
    public static PasswordResetRequestResult Accepted(bool emailSent) =>
        new(true, emailSent, null, StatusCodes.Status202Accepted);

    public static PasswordResetRequestResult Unavailable(string message) =>
        new(false, false, message, StatusCodes.Status503ServiceUnavailable);
}

public enum PasswordResetStatus
{
    Completed,
    InvalidToken,
    PasswordTooShort,
    PasswordUnchanged
}

public sealed record PasswordResetCompletionResult(PasswordResetStatus Status, string? ErrorMessage)
{
    public bool Succeeded => Status == PasswordResetStatus.Completed;
}
