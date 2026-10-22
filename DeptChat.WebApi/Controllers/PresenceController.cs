using System.Security.Claims;
using DeptChat.Application.Common.Interfaces;
using DeptChat.Infrastructure.Persistence;
using DeptChat.WebApi.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DeptChat.WebApi.Controllers;

[Authorize]
[ApiController]
[Route("api/presence")]
public class PresenceController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPresenceService _presenceService;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ILogger<PresenceController> _logger;

    public PresenceController(
        ApplicationDbContext dbContext,
        IPresenceService presenceService,
        IHubContext<ChatHub> hubContext,
        ILogger<PresenceController> logger)
    {
        _dbContext = dbContext;
        _presenceService = presenceService;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Explicitly marks the user as offline immediately (e.g. when app closes or task is removed).
    /// Eliminates the 60-second Anycast / WebSocket TCP timeout delay.
    /// </summary>
    [HttpPost("offline")]
    public async Task<IActionResult> SetOffline()
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();

        var currentPresence = await _presenceService.GetPresenceAsync(userId);
        await _presenceService.ClearPresenceAsync(userId);
        await _presenceService.SetUserOnlineAsync(userId, false);

        var now = DateTime.UtcNow;
        try
        {
            var user = await _dbContext.Users.FindAsync(userId);
            if (user != null)
            {
                user.LastSeenAt = now;
                await _dbContext.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist LastSeenAt for user {UserId}", userId);
        }

        // Broadcast to all clients immediately across the app
        await _hubContext.Clients.All.SendAsync("UserOnlineStatusChanged", userId, false);

        if (!string.IsNullOrEmpty(currentPresence) && currentPresence.StartsWith("room:"))
        {
            var convIdStr = currentPresence.Substring(5);
            if (Guid.TryParse(convIdStr, out var convId))
            {
                await _hubContext.Clients.Group($"Room_{convId}").SendAsync("UserPresenceChanged", convId, userId, false);
                await _hubContext.Clients.Group($"Room_{convId}").SendAsync("UserPresenceState", convId, userId, false, false, now);
            }
        }

        _logger.LogInformation("User {UserId} explicitly marked offline via REST", userId);
        return Ok(new { status = "offline", lastSeen = now });
    }

    /// <summary>
    /// Heartbeat or explicit online status update.
    /// </summary>
    [HttpPost("online")]
    public async Task<IActionResult> SetOnline()
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await _presenceService.SetUserOnlineAsync(userId, true);
        await _hubContext.Clients.All.SendAsync("UserOnlineStatusChanged", userId, true);

        return Ok(new { status = "online" });
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdClaim, out var id) ? id : Guid.Empty;
    }
}
