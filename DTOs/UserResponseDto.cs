namespace GradrTab.DTOs;


// Safe response object containing no sensitive user fields
public record UserResponseDto(Guid Id, string FirstName, string LastName, string Email, DateTime CreatedAt);