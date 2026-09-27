using System.ComponentModel.DataAnnotations;

namespace GradrTab.Models;

// One outstanding password reset request. Only the hash of the token is kept,
// so a leaked database row cannot be turned into a working reset link.
public class PasswordResetToken
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public User? User { get; set; }

    // SHA-256 of the emailed token, never the token itself
    [Required]
    [MaxLength(128)]
    public string TokenHash { get; set; } = string.Empty;

    // After this moment the token is refused
    public DateTime ExpiresAt { get; set; }

    // Set as soon as the token is used or superseded, a token can only be used once
    public DateTime? UsedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
