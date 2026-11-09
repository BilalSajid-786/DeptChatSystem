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

[Authorize(Roles = "Admin,Manager")]
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;

    public UsersController(ApplicationDbContext dbContext, IPasswordHasher passwordHasher)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
    }

    private Guid GetClientId()
    {
        var claim = User.FindFirstValue("client_id");
        if (claim != null && Guid.TryParse(claim, out var cid))
        {
            return cid;
        }
        return Guid.Parse("00000000-0000-0000-0000-000000000001");
    }

    /// <summary>
    /// Creates a new user inside the caller's client organization.
    /// Admins can create User, Manager, or Admin. Managers can only create standard User.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequestDto dto)
    {
        if (User.IsInRole("Manager") && dto.Role != UserRole.User)
        {
            return BadRequest(new { message = "Managers are only permitted to create standard users." });
        }

        if (string.IsNullOrWhiteSpace(dto.FullName))
        {
            return BadRequest(new { message = "Full name is required." });
        }

        if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 6)
        {
            return BadRequest(new { message = "Password must be at least 6 characters long." });
        }

        var hasUsername = !string.IsNullOrWhiteSpace(dto.Username);
        var hasEmail = !string.IsNullOrWhiteSpace(dto.Email);
        var hasPhone = !string.IsNullOrWhiteSpace(dto.PhoneNumber);

        if (!hasUsername && !hasEmail && !hasPhone)
        {
            return BadRequest(new { message = "Please provide a username, an email address, or a phone number." });
        }

        var clientId = GetClientId();

        string? normalizedUsername = null;
        if (hasUsername)
        {
            normalizedUsername = dto.Username!.Trim().ToLowerInvariant();
            if (normalizedUsername.Length < 3 || normalizedUsername.Length > 50)
            {
                return BadRequest(new { message = "Username must be between 3 and 50 characters." });
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(normalizedUsername, @"^[a-z0-9_.-]+$"))
            {
                return BadRequest(new { message = "Username can only contain letters, numbers, underscores, hyphens, and periods." });
            }

            var usernameExists = await _dbContext.Users.AnyAsync(u => u.ClientId == clientId && u.Username != null && u.Username == normalizedUsername);
            if (usernameExists)
            {
                return Conflict(new { message = "This username is already taken in your organization." });
            }
        }

        string? normalizedEmail = null;
        if (hasEmail)
        {
            normalizedEmail = dto.Email!.Trim().ToLowerInvariant();
            var emailExists = await _dbContext.Users.AnyAsync(u => u.Email != null && u.Email == normalizedEmail);
            if (emailExists)
            {
                return Conflict(new { message = "An account with this email address already exists." });
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

        var user = new User
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            FullName = dto.FullName.Trim(),
            Username = normalizedUsername,
            Email = normalizedEmail,
            PhoneNumber = normalizedPhone,
            PasswordHash = _passwordHasher.HashPassword(dto.Password),
            Role = dto.Role,
            Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim(),
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = null
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var summary = new UserSummaryDto(
            user.Id,
            user.FullName,
            user.Email,
            user.PhoneNumber,
            user.Role,
            user.Address,
            user.CreatedAt,
            user.IsBlocked,
            user.Username
        );

        return StatusCode(StatusCodes.Status201Created, summary);
    }

    /// <summary>
    /// Admin-only: Retrieves all users belonging to the Admin's client organization.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetUsers()
    {
        var clientId = GetClientId();

        var users = await _dbContext.Users
            .Where(u => u.ClientId == clientId)
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new UserSummaryDto(
                u.Id,
                u.FullName,
                u.Email,
                u.PhoneNumber,
                u.Role,
                u.Address,
                u.CreatedAt,
                u.IsBlocked,
                u.Username
            ))
            .ToListAsync();

        return Ok(users);
    }

    /// <summary>
    /// Blocks a user.
    /// Admin can block Users and Managers.
    /// Manager can ONLY block Users.
    /// </summary>
    [HttpPost("{id}/block")]
    public async Task<IActionResult> BlockUser(Guid id, [FromBody] BlockUserRequestDto? dto)
    {
        var callerRole = User.FindFirstValue(ClaimTypes.Role);
        var callerIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid.TryParse(callerIdClaim, out var callerId);
        var clientId = GetClientId();

        var targetUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id && u.ClientId == clientId);
        if (targetUser == null)
        {
            return NotFound(new { message = "User not found in your organization." });
        }

        // Managers can ONLY block Customers (Users)
        if (callerRole == UserRole.Manager.ToString() && targetUser.Role != UserRole.User)
        {
            return Forbid();
        }

        // Admins can block Users and Managers, but not other Admins or themselves
        if (targetUser.Role == UserRole.Admin || targetUser.Id == callerId)
        {
            return BadRequest(new { message = "Cannot block an administrator account." });
        }

        targetUser.IsBlocked = true;
        targetUser.BlockedAt = DateTime.UtcNow;
        targetUser.BlockedByUserId = callerId;
        await _dbContext.SaveChangesAsync();

        return Ok(new { message = $"User '{targetUser.FullName}' has been blocked successfully." });
    }

    /// <summary>
    /// Unblocks a user.
    /// </summary>
    [HttpPost("{id}/unblock")]
    public async Task<IActionResult> UnblockUser(Guid id)
    {
        var callerRole = User.FindFirstValue(ClaimTypes.Role);
        var clientId = GetClientId();

        var targetUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id && u.ClientId == clientId);
        if (targetUser == null)
        {
            return NotFound(new { message = "User not found in your organization." });
        }

        if (callerRole == UserRole.Manager.ToString() && targetUser.Role != UserRole.User)
        {
            return Forbid();
        }

        targetUser.IsBlocked = false;
        targetUser.BlockedAt = null;
        targetUser.BlockedByUserId = null;
        await _dbContext.SaveChangesAsync();

        return Ok(new { message = $"User '{targetUser.FullName}' has been unblocked successfully." });
    }

    /// <summary>
    /// Admin-only: Retrieves all Managers in the organization with their blocked status.
    /// </summary>
    [HttpGet("managers")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetManagers()
    {
        var clientId = GetClientId();
        var managers = await _dbContext.Users
            .Where(u => u.ClientId == clientId && u.Role == UserRole.Manager)
            .OrderBy(u => u.FullName)
            .Select(u => new UserSummaryDto(
                u.Id,
                u.FullName,
                u.Email,
                u.PhoneNumber,
                u.Role,
                u.Address,
                u.CreatedAt,
                u.IsBlocked
            ))
            .ToListAsync();

        return Ok(managers);
    }

    /// <summary>
    /// Admin-only: Resets the password of any Manager or User (Customer) in their organization.
    /// </summary>
    [HttpPost("{id}/reset-password")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] AdminResetPasswordRequestDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.NewPassword))
        {
            return BadRequest(new { message = "New password is required." });
        }

        if (dto.NewPassword.Length < 6)
        {
            return BadRequest(new { message = "New password must be at least 6 characters long." });
        }

        var clientId = GetClientId();
        var targetUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id && u.ClientId == clientId);
        if (targetUser == null)
        {
            return NotFound(new { message = "User not found in your organization." });
        }

        // Admins cannot reset passwords of other Admins through this endpoint
        if (targetUser.Role == UserRole.Admin)
        {
            return BadRequest(new { message = "Cannot reset the password of an administrator account." });
        }

        targetUser.PasswordHash = _passwordHasher.HashPassword(dto.NewPassword);
        await _dbContext.SaveChangesAsync();

        return Ok(new { message = $"Password for '{targetUser.FullName}' has been reset successfully." });
    }
}