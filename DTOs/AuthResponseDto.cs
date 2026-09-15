namespace GradrTab.DTOs;

// Sent to the client upon successful authentication
public record AuthResponseDto(UserResponseDto User, string Token);
