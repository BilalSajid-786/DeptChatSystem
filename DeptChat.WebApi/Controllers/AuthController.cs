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
    /// Public user self-registration with mandatory Company Code.
    /// Supports registration via Email OR Phone Number (or both).
    /// Strictly assigns Role = User (Privilege escalation prevention).
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.FullName) || string.IsNullOrWhiteSpace(dto.Password))
        {
            return BadRequest(new { message = "Full name and password are required." });
        }

        var hasEmail = !string.IsNullOrWhiteSpace(dto.Email);
        var hasPhone = !string.IsNullOrWhiteSpace(dto.PhoneNumber);

        if (!hasEmail && !hasPhone)
        {
            return BadRequest(new { message = "Please provide either an email address or a phone number to register." });
        }

        if (string.IsNullOrWhiteSpace(dto.CompanyCode))
        {
            return BadRequest(new { message = "Company code is required to register." });
        }

        if (dto.Password.Length < 6)
        {
            return BadRequest(new { message = "Password must be at least 6 characters long." });
        }

        // Validate Company Code
        var normalizedCode = dto.CompanyCode.Trim().ToUpperInvariant();
        var client = await _dbContext.Clients
            .FirstOrDefaultAsync(c => c.Code.ToUpper() == normalizedCode && c.IsActive);

        if (client == null)
        {
            return BadRequest(new { message = $"Company code '{dto.CompanyCode.Trim()}' was not found or is inactive. Please contact your administrator." });
        }

        string? normalizedEmail = null;
        if (hasEmail)
        {
            normalizedEmail = dto.Email!.Trim().ToLowerInvariant();
            var emailExists = await _dbContext.Users.AnyAsync(u => u.Email != null && u.Email == normalizedEmail);
            if (emailExists)
            {
                return Conflict(new { message = "An account with this email already exists." });
            }
        }

        string? normalizedPhone = null;
        if (hasPhone)
        {
            normalizedPhone = dto.PhoneNumber!.Trim();
            var phoneExists = await _dbContext.Users.AnyAsync(u => u.PhoneNumber != null && u.PhoneNumber == normalizedPhone);
            if (phoneExists)
            {
                return Conflict(new { message = "An account with this phone number already exists." });
            }
        }

        // Strictly enforce Role = User. Public signups can NEVER become Admin or Manager.
        var user = new User
        {
            Id = Guid.NewGuid(),
            ClientId = client.Id,
            FullName = dto.FullName.Trim(),
            Email = normalizedEmail,
            PhoneNumber = normalizedPhone,
            PasswordHash = _passwordHasher.HashPassword(dto.Password),
            Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim(),
            Role = UserRole.User,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var token = _jwtTokenService.GenerateToken(user.Id, user.FullName, user.Email, user.Role, client.Id, client.Name);

        var response = new AuthResponseDto(
            UserId: user.Id,
            FullName: user.FullName,
            Email: user.Email,
            Role: user.Role,
            Token: token,
            DepartmentIds: new List<Guid>(),
            ClientId: client.Id,
            ClientName: client.Name,
            PhoneNumber: user.PhoneNumber,
            Address: user.Address
        );

        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>
    /// Login for all roles (Admin, Manager, and User).
    /// Automatically identifies user's client workspace via Email or Phone Number.
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
        {
            return BadRequest(new { message = "Email or phone number, and password are required." });
        }

        var identifier = dto.Email.Trim();
        var normalizedEmail = identifier.ToLowerInvariant();

        var user = await _dbContext.Users
            .Include(u => u.Client)
            .Include(u => u.DepartmentAssignments)
            .FirstOrDefaultAsync(u => 
                (u.Email != null && u.Email == normalizedEmail) ||
                (u.PhoneNumber != null && u.PhoneNumber == identifier));

        if (user == null || !_passwordHasher.VerifyPassword(dto.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "Invalid email/phone or password." });
        }

        if (user.Client != null && (!user.Client.IsActive || user.Client.IsBlocked))
        {
            return Unauthorized(new { message = "Your organization workspace has been suspended or blocked. Please contact support." });
        }

        if (user.IsBlocked)
        {
            return Unauthorized(new { message = "Your account has been blocked by an administrator. Please contact support." });
        }

        var clientName = user.Client?.Name ?? "Main Organization";
        var clientId = user.ClientId;
        var departmentIds = user.DepartmentAssignments.Select(da => da.DepartmentId).ToList();

        var token = _jwtTokenService.GenerateToken(user.Id, user.FullName, user.Email, user.Role, clientId, clientName, departmentIds);

        var response = new AuthResponseDto(
            UserId: user.Id,
            FullName: user.FullName,
            Email: user.Email,
            Role: user.Role,
            Token: token,
            DepartmentIds: departmentIds,
            ClientId: clientId,
            ClientName: clientName,
            PhoneNumber: user.PhoneNumber,
            Address: user.Address
        );

        return Ok(response);
    }

    /// <summary>
    /// Returns the authenticated user's profile and company details.
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    [HttpGet("profile")]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users
            .Include(u => u.Client)
            .Include(u => u.DepartmentAssignments)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return NotFound(new { message = "User not found." });
        }

        return Ok(new UserProfileDto(
            UserId: user.Id,
            FullName: user.FullName,
            Email: user.Email,
            Role: user.Role,
            ClientId: user.ClientId,
            ClientName: user.Client?.Name ?? "Main Organization",
            PhoneNumber: user.PhoneNumber,
            Address: user.Address
        ));
    }

    /// <summary>
    /// Updates the authenticated user's profile details (FullName, PhoneNumber, Address).
    /// </summary>
    [Authorize]
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequestDto dto)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users
            .Include(u => u.Client)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return NotFound(new { message = "User not found." });
        }

        if (!string.IsNullOrWhiteSpace(dto.FullName))
        {
            user.FullName = dto.FullName.Trim();
        }

        user.PhoneNumber = string.IsNullOrWhiteSpace(dto.PhoneNumber) ? null : dto.PhoneNumber.Trim();
        user.Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim();

        await _dbContext.SaveChangesAsync();

        return Ok(new UserProfileDto(
            UserId: user.Id,
            FullName: user.FullName,
            Email: user.Email,
            Role: user.Role,
            ClientId: user.ClientId,
            ClientName: user.Client?.Name ?? "Main Organization",
            PhoneNumber: user.PhoneNumber,
            Address: user.Address
        ));
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

    /// <summary>
    /// Logs out the user and clears all registered device tokens from the database.
    /// </summary>
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userIdClaim != null && Guid.TryParse(userIdClaim, out var userId))
        {
            var tokens = await _dbContext.DeviceTokens.Where(dt => dt.UserId == userId).ToListAsync();
            if (tokens.Count > 0)
            {
                _dbContext.DeviceTokens.RemoveRange(tokens);
                await _dbContext.SaveChangesAsync();
            }
        }
        return Ok(new { message = "Logged out successfully and device tokens removed." });
    }
}

