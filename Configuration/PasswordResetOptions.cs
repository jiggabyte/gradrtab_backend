namespace GradrTab.Configuration;

// Bound from the "PasswordReset" section of appsettings.
public class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    // How long an emailed link stays usable
    public int TokenLifetimeMinutes { get; set; } = 30;

    // How many links one account may ask for per hour, stops mailbox flooding
    public int MaxRequestsPerHour { get; set; } = 5;

    // Where the emailed link points, this is the frontend page that asks for
    // the new password, not an API route.
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";

    // Shortest password a user may choose
    public int MinimumPasswordLength { get; set; } = 8;

    // Refuse a reset that would set the password back to the current one
    public bool RequireDifferentPassword { get; set; } = true;
}
