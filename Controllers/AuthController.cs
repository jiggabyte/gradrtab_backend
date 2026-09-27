using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using BC = BCrypt.Net.BCrypt;
using GradrTab.DTOs;
using GradrTab.Models;
using GradrTab.Repositories;
using GradrTab.Services;

namespace GradrTab.Controllers;

[ApiController]
[Route("api/v1/")]
public class AuthController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordResetService _passwordResetService;
    private readonly IConfiguration _configuration;

    public AuthController(
        IUnitOfWork unitOfWork,
        IPasswordResetService passwordResetService,
        IConfiguration configuration)
    {
        _unitOfWork = unitOfWork;
        _passwordResetService = passwordResetService;
        _configuration = configuration;
    }

    [HttpGet("health")]
    public IActionResult Test()
    {
        return Ok("API is running and healthy!");
    }

    [HttpPost("register")]
    public async Task<ActionResult<UserResponseDto>> Register(RegisterRequestDto request)
    {
        // Check if user already exists
        if (await _unitOfWork.Users.ExistsWithEmailAsync(request.Email.ToLower()))
        {
            return BadRequest("A user with this email already exists.");
        }

        // Hash the raw password securely using BCrypt
        string passwordHash = BC.HashPassword(request.Password);

        // Map DTO fields to the core domain User Model
        var user = new User
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email.ToLower(),
            PasswordHash = passwordHash
        };

        await _unitOfWork.Users.AddAsync(user);
        await _unitOfWork.SaveChangesAsync();

        // Issue a secure JWT string
        string token = GenerateJwtToken(user);

        // Return the safe UserResponseDto (without password) along with the token
        var userDto = new UserResponseDto(user.Id, user.FirstName, user.LastName, user.Email, user.CreatedAt);
        // return CreatedAtAction(nameof(Register), userDto); --- IGNORE ---
        return Ok(new AuthResponseDto(userDto, token));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginRequestDto request)
    {
        // Look up user by email
        var user = await _unitOfWork.Users.GetByEmailAsync(request.Email.ToLower());
        if (user == null)
        {
            return Unauthorized("Invalid email or password.");
        }

        // Verify the raw password entry matches the securely hashed database record
        if (!BC.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized("Invalid email or password.");
        }

        // Issue a secure JWT string
        string token = GenerateJwtToken(user);

        // Send back the combined User details and authentication token
        var userDto = new UserResponseDto(user.Id, user.FirstName, user.LastName, user.Email, user.CreatedAt);
        return Ok(new AuthResponseDto(userDto, token));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserResponseDto>> GetCurrentUser()
    {
        // Extract the user ID from the JWT claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
        {
            return Unauthorized("Invalid token: missing or malformed user ID.");
        }

        // Fetch the user from the database
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null)
        {
            return NotFound("User not found.");
        }
        var userDto = new UserResponseDto(user.Id, user.FirstName, user.LastName, user.Email, user.CreatedAt);
        return Ok(new AuthResponseDto(userDto, ""));
    }

    // POST /api/v1/forgot-password
    // Always answers 202 with the same message, whether or not the address is
    // registered, so this cannot be used to find out which emails have accounts.
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequestDto request)
    {
        var result = await _passwordResetService.RequestResetAsync(request.Email);

        // 503 means SMTP is not configured, that is a server problem worth showing
        if (!result.Succeeded)
        {
            return StatusCode(result.StatusCode, new { message = result.ErrorMessage });
        }

        return Accepted(new
        {
            message = "If an account exists for that address, a reset link is on its way."
        });
    }

    // POST /api/v1/reset-password
    // Redeems the token from the emailed link and stores a new BCrypt hash.
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequestDto request)
    {
        var result = await _passwordResetService.CompleteResetAsync(request.Token, request.NewPassword);

        if (result.Succeeded)
        {
            return Ok(new { message = "Your password has been changed. You can sign in with it now." });
        }

        return BadRequest(new { message = result.ErrorMessage });
    }

    private string GenerateJwtToken(User user)
    {
        var jwtSettings = _configuration.GetSection("JwtSettings");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Define claims payload stored inside the token
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.GivenName, user.FirstName),
            new Claim(ClaimTypes.Surname, user.LastName)
        };

        var token = new JwtSecurityToken(
            issuer: jwtSettings["Issuer"],
            audience: jwtSettings["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(double.Parse(jwtSettings["DurationInMinutes"]!)),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
