using System.Security.Claims;
using DeptChat.Domain.Entities;
using DeptChat.Infrastructure.Persistence;
using DeptChat.Shared.DTOs;
using DeptChat.WebApi.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DeptChat.WebApi.Controllers;

/// <summary>
/// Company (client organization) management. Only admins of the platform-owner company may use it
/// (setting Platform:OwnerCompanyCode, from the server's .env). Admins of every other company get 403,
/// so no customer company can see or block another one.
/// </summary>
[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/clients")]
public class ClientsController : ControllerBase
{
    // Shown on the forced logout and (like today) at login when a company is blocked.
    public const string CompanySuspendedMessage = "Your organization workspace has been suspended. Please contact support.";

    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _memoryCache;
    private readonly IHubContext<ChatHub> _hubContext;

    public ClientsController(ApplicationDbContext dbContext, IConfiguration configuration, IMemoryCache memoryCache, IHubContext<ChatHub> hubContext)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _memoryCache = memoryCache;
        _hubContext = hubContext;
    }

    /// <summary>True when the company code is the platform owner's (Platform:OwnerCompanyCode). No setting = nobody.</summary>
    public static bool IsPlatformOwnerCompany(IConfiguration configuration, string? companyCode)
    {
        var ownerCode = configuration["Platform:OwnerCompanyCode"];
        return !string.IsNullOrWhiteSpace(ownerCode)
               && !string.IsNullOrWhiteSpace(companyCode)
               && string.Equals(ownerCode.Trim(), companyCode.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The caller's company if the caller is an admin of the platform-owner company, else null.</summary>
    private async Task<Client?> GetPlatformCompanyOfCallerAsync()
    {
        if (!Guid.TryParse(User.FindFirstValue("client_id"), out var callerClientId)) return null;
        var company = await _dbContext.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == callerClientId);
        return company != null && IsPlatformOwnerCompany(_configuration, company.Code) ? company : null;
    }

    /// <summary>Platform admins: all client organizations.</summary>
    [HttpGet]
    public async Task<IActionResult> GetClients()
    {
        if (await GetPlatformCompanyOfCallerAsync() == null) return Forbid();

        var clients = await _dbContext.Clients
            .OrderBy(c => c.Name)
            .Select(c => new ClientSummaryDto(
                c.Id,
                c.Name,
                c.Code,
                c.Description,
                c.IsActive,
                c.IsBlocked,
                c.CreatedAt
            ))
            .ToListAsync();

        return Ok(clients);
    }

    /// <summary>
    /// Platform admins: blocks a client organization. All its users are logged out on every device at once
    /// and can't log in or send until it is unblocked. The platform company can't block itself.
    /// </summary>
    [HttpPost("{id}/block")]
    public async Task<IActionResult> BlockClient(Guid id)
    {
        var platform = await GetPlatformCompanyOfCallerAsync();
        if (platform == null) return Forbid();
        if (platform.Id == id) return BadRequest(new { message = "The platform company can't block itself." });

        var client = await _dbContext.Clients.FindAsync(id);
        if (client == null)
        {
            return NotFound(new { message = "Client organization not found." });
        }

        client.IsBlocked = true;
        client.IsActive = false;
        client.BlockedAt = DateTime.UtcNow;

        // Log every user of the company out at once (same steps as blocking a user): new security stamps,
        // sessions revoked, cached checks dropped, no more pushes.
        var users = await _dbContext.Users.Where(u => u.ClientId == id).ToListAsync();
        var userIds = users.Select(u => u.Id).ToList();
        foreach (var u in users) u.SecurityStamp = Guid.NewGuid().ToString("N");
        var sessions = await _dbContext.UserSessions.Where(s => userIds.Contains(s.UserId) && s.IsActive).ToListAsync();
        foreach (var s in sessions)
        {
            s.IsActive = false;
            s.RevokedAt = DateTime.UtcNow;
            s.RevokedReason = "Company Blocked";
            _memoryCache.Remove($"user_session_active_{s.Id}");
        }
        var tokens = await _dbContext.DeviceTokens.Where(dt => userIds.Contains(dt.UserId)).ToListAsync();
        _dbContext.DeviceTokens.RemoveRange(tokens);
        await _dbContext.SaveChangesAsync();
        foreach (var userId in userIds) _memoryCache.Remove($"user_security_stamp_{userId}");

        try
        {
            await _hubContext.Clients.Group($"Client_{id}").SendAsync("ForceLogout", CompanySuspendedMessage);
        }
        catch { }

        return Ok(new { message = $"Client organization '{client.Name}' has been blocked. {userIds.Count} user(s) were signed out." });
    }

    /// <summary>Platform admins: unblocks a client organization (its users can log in again).</summary>
    [HttpPost("{id}/unblock")]
    public async Task<IActionResult> UnblockClient(Guid id)
    {
        if (await GetPlatformCompanyOfCallerAsync() == null) return Forbid();

        var client = await _dbContext.Clients.FindAsync(id);
        if (client == null)
        {
            return NotFound(new { message = "Client organization not found." });
        }

        client.IsBlocked = false;
        client.IsActive = true;
        client.BlockedAt = null;
        await _dbContext.SaveChangesAsync();

        return Ok(new { message = $"Client organization '{client.Name}' has been unblocked." });
    }
}
