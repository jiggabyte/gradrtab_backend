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

namespace GradrTab.Controllers;

[ApiController]
[Route("api/v1/")]
public class AuthController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConfiguration _configuration;

    public AuthController(IUnitOfWork unitOfWork, IConfiguration configuration)
    {
        _unitOfWork = unitOfWork;
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
        // 1. Check if user already exists
        if (await _unitOfWork.Users.ExistsWithEmailAsync(request.Email.ToLower()))
        {
            return BadRequest("A user with this email already exists.");
        }

        // 2. Hash the raw password securely using BCrypt
        string passwordHash = BC.HashPassword(request.Password);

        // 3. Map DTO fields to the core domain User Model
        var user = new User
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email.ToLower(),
            PasswordHash = passwordHash
        };

        await _unitOfWork.Users.AddAsync(user);
        await _unitOfWork.SaveChangesAsync();

        // 4. Return the safe UserResponseDto
        var response = new UserResponseDto(user.Id, user.FirstName, user.LastName, user.Email, user.CreatedAt);
        return CreatedAtAction(nameof(Register), response);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginRequestDto request)
    {
        // 1. Look up user by email
        var user = await _unitOfWork.Users.GetByEmailAsync(request.Email.ToLower());
        if (user == null)
        {
            return Unauthorized("Invalid email or password.");
        }

        // 2. Verify the raw password entry matches the securely hashed database record
        if (!BC.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized("Invalid email or password.");
        }

        // 3. Issue a secure JWT string
        string token = GenerateJwtToken(user);

        // 4. Send back the combined User details and authentication token
        var userDto = new UserResponseDto(user.Id, user.FirstName, user.LastName, user.Email, user.CreatedAt);
        return Ok(new AuthResponseDto(userDto, token));
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
