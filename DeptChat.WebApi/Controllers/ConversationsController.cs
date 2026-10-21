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

[Authorize]
[ApiController]
[Route("api/conversations")]
public class ConversationsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public ConversationsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// For a regular user: Gets or creates their single persistent conversation with a department.
    /// For a manager or admin: Lists all user conversations in this department.
    /// </summary>
    [HttpGet("department/{departmentId}")]
    public async Task<IActionResult> GetDepartmentConversations(Guid departmentId)
    {
        var userId = GetUserId();
        var role = GetUserRole();

        var department = await _dbContext.Departments.FirstOrDefaultAsync(d => d.Id == departmentId && d.IsActive);
        if (department == null)
        {
            return NotFound(new { message = "Department not found." });
        }

        if (role == UserRole.User)
        {
            // For a regular user, get or create the single conversation with this department
            var conversation = await _dbContext.Conversations
                .Include(c => c.CreatedByUser)
                .Include(c => c.Department)
                .FirstOrDefaultAsync(c => c.DepartmentId == departmentId && c.CreatedByUserId == userId);

            if (conversation == null)
            {
                var user = await _dbContext.Users.FindAsync(userId);
                conversation = new Conversation
                {
                    Id = Guid.NewGuid(),
                    DepartmentId = departmentId,
                    CreatedByUserId = userId,
                    Status = ConversationStatus.Active,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _dbContext.Conversations.Add(conversation);
                await _dbContext.SaveChangesAsync();
                conversation.CreatedByUser = user!;
                conversation.Department = department;
            }

            var unreadCount = await _dbContext.Messages
                .Where(m => m.ConversationId == conversation.Id && m.SenderId != userId && m.Status < MessageDeliveryStatus.Read)
                .CountAsync();

            var summary = new ConversationSummaryDto(
                conversation.Id,
                conversation.DepartmentId,
                department.Name,
                conversation.CreatedByUserId,
                conversation.CreatedByUser.FullName,
                conversation.LastMessagePreview,
                conversation.LastMessageAt,
                unreadCount,
                conversation.Status
            );

            return Ok(new List<ConversationSummaryDto> { summary });
        }
        else // Manager or Admin
        {
            if (role == UserRole.Manager)
            {
                var hasExplicitManagers = await _dbContext.DepartmentManagers
                    .AnyAsync(dm => dm.DepartmentId == departmentId);
                if (hasExplicitManagers)
                {
                    var isAssigned = await _dbContext.DepartmentManagers
                        .AnyAsync(dm => dm.DepartmentId == departmentId && dm.UserId == userId);
                    if (!isAssigned)
                    {
                        return Forbid();
                    }
                }
            }

            var conversations = await _dbContext.Conversations
                .Include(c => c.CreatedByUser)
                .Include(c => c.Department)
                .Where(c => c.DepartmentId == departmentId)
                .OrderByDescending(c => c.UpdatedAt)
                .ToListAsync();

            var convIds = conversations.Select(c => c.Id).ToList();
            var unreadCounts = await _dbContext.Messages
                .Where(m => convIds.Contains(m.ConversationId) && m.SenderRole == UserRole.User && m.Status < MessageDeliveryStatus.Read)
                .GroupBy(m => m.ConversationId)
                .Select(g => new { ConversationId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ConversationId, x => x.Count);

            var list = conversations.Select(conv => new ConversationSummaryDto(
                conv.Id,
                conv.DepartmentId,
                conv.Department.Name,
                conv.CreatedByUserId,
                conv.CreatedByUser.FullName,
                conv.LastMessagePreview,
                conv.LastMessageAt,
                unreadCounts.GetValueOrDefault(conv.Id, 0),
                conv.Status
            )).ToList();

            return Ok(list);
        }
    }

    /// <summary>
    /// Gets conversation metadata by ID with access control.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetConversationById(Guid id)
    {
        var userId = GetUserId();
        var role = GetUserRole();

        var conv = await _dbContext.Conversations
            .Include(c => c.Department)
            .Include(c => c.CreatedByUser)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (conv == null)
        {
            return NotFound(new { message = "Conversation not found." });
        }

        if (role == UserRole.User && conv.CreatedByUserId != userId)
        {
            return Forbid();
        }

        if (role == UserRole.Manager)
        {
            var hasExplicitManagers = await _dbContext.DepartmentManagers
                .AnyAsync(dm => dm.DepartmentId == conv.DepartmentId);
            if (hasExplicitManagers)
            {
                var isAssigned = await _dbContext.DepartmentManagers
                    .AnyAsync(dm => dm.DepartmentId == conv.DepartmentId && dm.UserId == userId);
                if (!isAssigned)
                {
                    return Forbid();
                }
            }
        }

        var unreadCount = await _dbContext.Messages
            .Where(m => m.ConversationId == conv.Id && m.SenderId != userId && m.Status < MessageDeliveryStatus.Read)
            .CountAsync();

        return Ok(new ConversationSummaryDto(
            conv.Id,
            conv.DepartmentId,
            conv.Department.Name,
            conv.CreatedByUserId,
            conv.CreatedByUser.FullName,
            conv.LastMessagePreview,
            conv.LastMessageAt,
            unreadCount,
            conv.Status
        ));
    }

    /// <summary>
    /// Delta Catch-Up Sync: returns all messages strictly strictly after sinceSequenceNumber.
    /// Guarantees zero message loss during reconnects.
    /// </summary>
    [HttpGet("{id}/sync")]
    public async Task<IActionResult> SyncDelta(Guid id, [FromQuery] long sinceSequenceNumber = 0)
    {
        var userId = GetUserId();
        var role = GetUserRole();

        var conv = await _dbContext.Conversations.FirstOrDefaultAsync(c => c.Id == id);
        if (conv == null)
        {
            return NotFound(new { message = "Conversation not found." });
        }

        if (role == UserRole.User && conv.CreatedByUserId != userId)
        {
            return Forbid();
        }

        if (role == UserRole.Manager)
        {
            var hasExplicitManagers = await _dbContext.DepartmentManagers
                .AnyAsync(dm => dm.DepartmentId == conv.DepartmentId);
            if (hasExplicitManagers)
            {
                var isAssigned = await _dbContext.DepartmentManagers
                    .AnyAsync(dm => dm.DepartmentId == conv.DepartmentId && dm.UserId == userId);
                if (!isAssigned)
                {
                    return Forbid();
                }
            }
        }

        var messages = await _dbContext.Messages
            .Include(m => m.Sender)
            .Where(m => m.ConversationId == id && m.SequenceNumber > sinceSequenceNumber)
            .OrderBy(m => m.SequenceNumber)
            .ToListAsync();

        var lastSeq = messages.Count > 0 ? messages.Max(m => m.SequenceNumber) : sinceSequenceNumber;

        var messageDtos = messages.Select(m => new ChatMessageDto(
            m.Id,
            m.ConversationId,
            m.SenderId,
            m.Sender?.FullName ?? "Staff",
            m.SenderRole,
            m.ClientGeneratedId,
            m.SequenceNumber,
            m.Content,
            m.Status,
            m.SentAt
        )).ToList();

        return Ok(new ConversationSyncResponseDto(id, lastSeq, messageDtos));
    }

    /// <summary>
    /// Registers or updates device token for FCM push notifications.
    /// </summary>
    [HttpPost("register-device")]
    public async Task<IActionResult> RegisterDevice([FromBody] RegisterDeviceTokenDto dto)
    {
        var userId = GetUserId();

        var existing = await _dbContext.DeviceTokens.FirstOrDefaultAsync(d => d.Token == dto.Token);
        if (existing != null)
        {
            existing.UserId = userId;
            existing.Platform = dto.Platform;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _dbContext.DeviceTokens.Add(new DeviceToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Token = dto.Token,
                Platform = dto.Platform,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync();
        return Ok(new { message = "Device token registered successfully." });
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdClaim, out var id) ? id : Guid.Empty;
    }

    private UserRole GetUserRole()
    {
        var roleClaim = User.FindFirstValue(ClaimTypes.Role);
        return Enum.TryParse<UserRole>(roleClaim, out var role) ? role : UserRole.User;
    }
}
