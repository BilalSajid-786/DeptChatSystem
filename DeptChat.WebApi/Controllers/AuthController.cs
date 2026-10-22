using System.Security.Claims;
using DeptChat.Application.Common.Interfaces;
using DeptChat.Domain.Entities;
using DeptChat.Infrastructure.Persistence;
using DeptChat.Shared.DTOs;
using DeptChat.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DeptChat.WebApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;

    public AuthController(
        ApplicationDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    /// <summary>
    /// Public user self-registration.
    /// Strictly assigns Role = User (Privilege escalation prevention).
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.FullName) || string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
        {
            return BadRequest(new { message = "Full name, email, and password are required." });
        }

        if (dto.Password.Length < 6)
        {
            return BadRequest(new { message = "Password must be at least 6 characters long." });
        }

        var normalizedEmail = dto.Email.Trim().ToLowerInvariant();

        var emailExists = await _dbContext.Users.AnyAsync(u => u.Email == normalizedEmail);
        if (emailExists)
        {
            return Conflict(new { message = "An account with this email already exists." });
        }

        // Strictly enforce Role = User. Public signups can NEVER become Admin or Manager.
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = dto.FullName.Trim(),
            Email = normalizedEmail,
            PasswordHash = _passwordHasher.HashPassword(dto.Password),
            Role = UserRole.User,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var token = _jwtTokenService.GenerateToken(user.Id, user.FullName, user.Email, user.Role);

        var response = new AuthResponseDto(
            UserId: user.Id,
            FullName: user.FullName,
            Email: user.Email,
            Role: user.Role,
            Token: token,
            DepartmentIds: new List<Guid>()
        );

        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>
    /// Login for all roles (Admin, Manager, and User).
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
        {
            return BadRequest(new { message = "Email and password are required." });
        }

        var normalizedEmail = dto.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .Include(u => u.DepartmentAssignments)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail);

        if (user == null || !_passwordHasher.VerifyPassword(dto.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var departmentIds = user.DepartmentAssignments.Select(da => da.DepartmentId).ToList();

        var token = _jwtTokenService.GenerateToken(user.Id, user.FullName, user.Email, user.Role, departmentIds);

        var response = new AuthResponseDto(
            UserId: user.Id,
            FullName: user.FullName,
            Email: user.Email,
            Role: user.Role,
            Token: token,
            DepartmentIds: departmentIds
        );

        return Ok(response);
    }

    /// <summary>
    /// Returns the authenticated user's profile and claims.
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users
            .Include(u => u.DepartmentAssignments)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return NotFound(new { message = "User not found." });
        }

        var departmentIds = user.DepartmentAssignments.Select(da => da.DepartmentId).ToList();

        return Ok(new
        {
            user.Id,
            user.FullName,
            user.Email,
            Role = user.Role.ToString(),
            DepartmentIds = departmentIds
        });
    }

    /// <summary>
    /// Registers or updates a device token (FCM push notification token) for the authenticated user.
    /// </summary>
    [Authorize]
    [HttpPost("device-token")]
    public async Task<IActionResult> RegisterDeviceToken([FromBody] RegisterDeviceTokenDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Token))
        {
            return BadRequest(new { message = "Device token is required." });
        }

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var existingToken = await _dbContext.DeviceTokens
            .FirstOrDefaultAsync(dt => dt.UserId == userId && dt.Token == dto.Token.Trim());

        if (existingToken != null)
        {
            existingToken.UpdatedAt = DateTime.UtcNow;
            existingToken.Platform = string.IsNullOrWhiteSpace(dto.Platform) ? "Android" : dto.Platform.Trim();
        }
        else
        {
            _dbContext.DeviceTokens.Add(new DeviceToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Token = dto.Token.Trim(),
                Platform = string.IsNullOrWhiteSpace(dto.Platform) ? "Android" : dto.Platform.Trim(),
                UpdatedAt = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync();
        return Ok(new { message = "Device token registered successfully." });
    }
}
