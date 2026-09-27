using System.ComponentModel.DataAnnotations;

namespace GradrTab.DTOs;

// Body of POST /api/v1/forgot-password
public class ForgotPasswordRequestDto
{
    [Required]
    [EmailAddress]
    [StringLength(255)]
    public string Email { get; set; } = string.Empty;
}

// Body of POST /api/v1/reset-password
public class ResetPasswordRequestDto
{
    [Required]
    [StringLength(200, MinimumLength = 20)]
    public string Token { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string NewPassword { get; set; } = string.Empty;

    // Must match NewPassword, stops a typo from locking the user out
    [Compare(nameof(NewPassword), ErrorMessage = "The two passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
