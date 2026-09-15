namespace GradrTab.DTOs;

// Used when a client registers a new account
public record RegisterRequestDto(string FirstName, string LastName, string Email, string Password);