using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FinanceApp.API.Services;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private const string InvalidCredentialsMessage = "Invalid credentials";

    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly IUserSecurityService _securityService;

    public AuthController(AppDbContext context, IConfiguration config, IUserSecurityService securityService)
    {
        _context = context;
        _config = config;
        _securityService = securityService;
    }

    [HttpOptions("login")]
    public IActionResult LoginOptions() => Ok();

    [HttpPost("login")]
    [EnableRateLimiting("AuthLogin")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        var identifier = dto.GetIdentifier();
        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(dto.Password))
        {
            return Unauthorized(InvalidCredentialsMessage);
        }

        var normalizedIdentifier = _securityService.NormalizeIdentifier(identifier);
        var user = await _context.Users.FirstOrDefaultAsync(u =>
            u.NormalizedEmail == normalizedIdentifier || u.NormalizedUsername == normalizedIdentifier);

        if (user == null)
        {
            return Unauthorized(InvalidCredentialsMessage);
        }

        var verificationResult = _securityService.VerifyAndUpgradePassword(user, dto.Password, out var upgradedHash);
        if (verificationResult == Microsoft.AspNetCore.Identity.PasswordVerificationResult.Failed)
        {
            return Unauthorized(InvalidCredentialsMessage);
        }

        if (!string.IsNullOrWhiteSpace(upgradedHash))
        {
            user.PasswordHash = upgradedHash;
            await _context.SaveChangesAsync();
        }

        var tokenText = BuildToken(user);
        var me = new MeUserDto(user.Id, user.Username, user.Email, user.CreatedAt);
        return Ok(new LoginResponseDto(tokenText, DateTime.UtcNow.AddDays(7), me));
    }

    private string BuildToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Username)
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public class LoginDto
{
    public string? Identifier { get; set; }
    public string? Email { get; set; }
    public string Password { get; set; } = string.Empty;

    public string GetIdentifier() => (Identifier ?? Email ?? string.Empty).Trim();
}

public record LoginResponseDto(string Token, DateTime Expires, MeUserDto User);
