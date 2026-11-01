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
    private readonly IPresenceService _presenceService;

    public ConversationsController(ApplicationDbContext dbContext, IPresenceService presenceService)
    {
        _dbContext = dbContext;
        _presenceService = presenceService;
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
    /// For a regular user: Gets or creates their single persistent conversation with a department.
    /// For a manager or admin: Lists all user conversations in this department.
    /// Strictly scoped to caller's ClientId.
    /// </summary>
    [HttpGet("department/{departmentId}")]
    public async Task<IActionResult> GetDepartmentConversations(Guid departmentId)
    {
        var userId = GetUserId();
        var role = GetUserRole();
        var clientId = GetClientId();

        var department = await _dbContext.Departments
            .FirstOrDefaultAsync(d => d.Id == departmentId && d.ClientId == clientId && d.IsActive);

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
                .FirstOrDefaultAsync(c => c.DepartmentId == departmentId && c.CreatedByUserId == userId && c.ClientId == clientId);

            if (conversation == null)
            {
                var user = await _dbContext.Users.FindAsync(userId);
                conversation = new Conversation
                {
                    Id = Guid.NewGuid(),
                    ClientId = clientId,
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

            var isUserOnline = await _presenceService.IsUserOnlineAsync(userId);

            var summary = new ConversationSummaryDto(
                conversation.Id,
                conversation.DepartmentId,
                department.Name,
                conversation.CreatedByUserId,
                conversation.CreatedByUser.FullName,
                SanitizePreview(conversation.LastMessagePreview),
                conversation.LastMessageAt,
                unreadCount,
                conversation.Status,
                isUserOnline
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
                .Where(c => c.DepartmentId == departmentId && c.ClientId == clientId && _dbContext.Messages.Any(m => m.ConversationId == c.Id))
                .OrderByDescending(c => c.UpdatedAt)
                .ToListAsync();

            var convIds = conversations.Select(c => c.Id).ToList();
            var unreadCounts = await _dbContext.Messages
                .Where(m => convIds.Contains(m.ConversationId) && m.SenderRole == UserRole.User && m.Status < MessageDeliveryStatus.Read)
                .GroupBy(m => m.ConversationId)
                .Select(g => new { ConversationId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ConversationId, x => x.Count);

            var list = new List<ConversationSummaryDto>();
            foreach (var conv in conversations)
            {
                var isOnline = await _presenceService.IsUserOnlineAsync(conv.CreatedByUserId);
                list.Add(new ConversationSummaryDto(
                    conv.Id,
                    conv.DepartmentId,
                    conv.Department.Name,
                    conv.CreatedByUserId,
                    conv.CreatedByUser.FullName,
                    SanitizePreview(conv.LastMessagePreview),
                    conv.LastMessageAt,
                    unreadCounts.GetValueOrDefault(conv.Id, 0),
                    conv.Status,
                    isOnline,
                    conv.CreatedByUser?.IsBlocked ?? false
                ));
            }

            return Ok(list);
        }
    }

    private static string SanitizePreview(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "No messages yet";
        if (raw.StartsWith("[voice:", StringComparison.OrdinalIgnoreCase)) return "🎙️ Voice message";
        if (raw.StartsWith("[image:", StringComparison.OrdinalIgnoreCase)) return "📷 Photo";
        if (raw.StartsWith("[file:", StringComparison.OrdinalIgnoreCase))
        {
            var inner = raw.Trim('[', ']');
            var parts = inner.Split(':');
            return (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2])) ? $"📄 {parts[2]}" : "📎 Document";
        }
        return raw;
    }

    /// <summary>
    /// Gets conversation metadata by ID with access control.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetConversationById(Guid id)
    {
        var userId = GetUserId();
        var role = GetUserRole();
        var clientId = GetClientId();

        var conv = await _dbContext.Conversations
            .Include(c => c.Department)
            .Include(c => c.CreatedByUser)
            .FirstOrDefaultAsync(c => c.Id == id && c.ClientId == clientId);

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

        var isOnline = await _presenceService.IsUserOnlineAsync(conv.CreatedByUserId);

        return Ok(new ConversationSummaryDto(
            conv.Id,
            conv.DepartmentId,
            conv.Department.Name,
            conv.CreatedByUserId,
            conv.CreatedByUser.FullName,
            conv.LastMessagePreview,
            conv.LastMessageAt,
            unreadCount,
            conv.Status,
            isOnline
        ));
    }

    /// <summary>
    /// Paginated message retrieval: loads a window of messages.
    /// If beforeSequenceNumber is not specified, returns the latest messages (page of size pageSize).
    /// If beforeSequenceNumber is specified, returns messages strictly before that sequence number.
    /// </summary>
    [HttpGet("{id}/messages")]
    public async Task<IActionResult> GetMessages(
        Guid id,
        [FromQuery] long? beforeSequenceNumber = null,
        [FromQuery] int pageSize = 25)
    {
        var userId = GetUserId();
        var role = GetUserRole();
        var clientId = GetClientId();

        var conv = await _dbContext.Conversations.FirstOrDefaultAsync(c => c.Id == id && c.ClientId == clientId);
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

        pageSize = Math.Clamp(pageSize, 5, 100);

        IQueryable<Message> query = _dbContext.Messages
            .Include(m => m.Sender)
            .Where(m => m.ConversationId == id);

        if (beforeSequenceNumber.HasValue && beforeSequenceNumber.Value > 0)
        {
            query = query.Where(m => m.SequenceNumber < beforeSequenceNumber.Value);
        }

        var rawMessages = await query
            .OrderByDescending(m => m.SequenceNumber)
            .Take(pageSize + 1)
            .ToListAsync();

        var hasMore = rawMessages.Count > pageSize;
        var pagedList = rawMessages.Take(pageSize).OrderBy(m => m.SequenceNumber).ToList();

        var messageDtos = pagedList.Select(m => new ChatMessageDto(
            m.Id,
            m.ConversationId,
            m.SenderId,
            m.Sender?.FullName ?? "Staff",
            (m.Sender != null) ? m.Sender.Role : m.SenderRole,
            m.ClientGeneratedId,
            m.SequenceNumber,
            m.Content,
            m.Status,
            m.SentAt,
            m.IsDeleted,
            m.IsForwarded
        )).ToList();

        long? earliestSeq = messageDtos.Count > 0 ? messageDtos.Min(m => m.SequenceNumber) : null;
        long? latestSeq = messageDtos.Count > 0 ? messageDtos.Max(m => m.SequenceNumber) : null;

        return Ok(new PaginatedMessagesDto(id, earliestSeq, latestSeq, hasMore, messageDtos));
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
        var clientId = GetClientId();

        var conv = await _dbContext.Conversations.FirstOrDefaultAsync(c => c.Id == id && c.ClientId == clientId);
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

        var serverMaxSeq = await _dbContext.Messages
            .Where(m => m.ConversationId == id)
            .MaxAsync(m => (long?)m.SequenceNumber) ?? 0;

        var lastSeq = messages.Count > 0 ? messages.Max(m => m.SequenceNumber) : serverMaxSeq;

        var messageDtos = messages.Select(m => new ChatMessageDto(
            m.Id,
            m.ConversationId,
            m.SenderId,
            m.Sender?.FullName ?? "Staff",
            (m.Sender != null) ? m.Sender.Role : m.SenderRole,
            m.ClientGeneratedId,
            m.SequenceNumber,
            m.Content,
            m.Status,
            m.SentAt,
            m.IsDeleted,
            m.IsForwarded
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

    /// <summary>
    /// For Manager or Admin: returns all registered users available to start a chat with in this department.
    /// Strictly filtered to the caller's Client organization.
    /// </summary>
    [HttpGet("department/{departmentId}/available-users")]
    public async Task<IActionResult> GetAvailableUsers(Guid departmentId)
    {
        var userId = GetUserId();
        var role = GetUserRole();
        var clientId = GetClientId();

        if (role != UserRole.Admin && role != UserRole.Manager)
        {
            return Forbid();
        }

        var department = await _dbContext.Departments.FirstOrDefaultAsync(d => d.Id == departmentId && d.ClientId == clientId && d.IsActive);
        if (department == null)
        {
            return NotFound(new { message = "Department not found." });
        }

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

        // Get all active users (customers) belonging strictly to the SAME client organization
        var users = await _dbContext.Users
            .Where(u => u.ClientId == clientId && u.Role == UserRole.User && u.Id != userId)
            .OrderBy(u => u.FullName)
            .ToListAsync();

        var existingConvUserIds = await _dbContext.Conversations
            .Where(c => c.DepartmentId == departmentId && c.ClientId == clientId && _dbContext.Messages.Any(m => m.ConversationId == c.Id))
            .Select(c => c.CreatedByUserId)
            .ToHashSetAsync();

        // Users who haven't started chatting in this department yet
        var availableUsers = users.Where(u => !existingConvUserIds.Contains(u.Id)).ToList();

        var result = new List<AvailableDepartmentUserDto>();
        foreach (var u in availableUsers)
        {
            var isOnline = await _presenceService.IsUserOnlineAsync(u.Id);
            result.Add(new AvailableDepartmentUserDto(
                u.Id,
                u.FullName,
                u.Email ?? u.PhoneNumber ?? "",
                u.Role,
                isOnline,
                false,
                u.IsBlocked
            ));
        }

        // Sort online users to top, then alphabetical
        return Ok(result.OrderByDescending(u => u.IsOnline).ThenBy(u => u.FullName).ToList());
    }

    /// <summary>
    /// For Manager or Admin: starts or opens a persistent conversation with a target user in this department.
    /// Strictly assigns ClientId.
    /// </summary>
    [HttpPost("start-with-user")]
    public async Task<IActionResult> StartConversationWithUser([FromBody] StartConversationWithUserRequest request)
    {
        var staffUserId = GetUserId();
        var role = GetUserRole();
        var clientId = GetClientId();

        if (role != UserRole.Admin && role != UserRole.Manager)
        {
            return Forbid();
        }

        var department = await _dbContext.Departments.FirstOrDefaultAsync(d => d.Id == request.DepartmentId && d.ClientId == clientId && d.IsActive);
        if (department == null)
        {
            return NotFound(new { message = "Department not found." });
        }

        var targetUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == request.TargetUserId && u.ClientId == clientId);
        if (targetUser == null)
        {
            return NotFound(new { message = "Target user not found in this organization." });
        }

        if (role == UserRole.Manager)
        {
            var hasExplicitManagers = await _dbContext.DepartmentManagers
                .AnyAsync(dm => dm.DepartmentId == request.DepartmentId);
            if (hasExplicitManagers)
            {
                var isAssigned = await _dbContext.DepartmentManagers
                    .AnyAsync(dm => dm.DepartmentId == request.DepartmentId && dm.UserId == staffUserId);
                if (!isAssigned)
                {
                    return Forbid();
                }
            }
        }

        // Check if conversation already exists for this (departmentId, targetUserId, clientId)
        var conversation = await _dbContext.Conversations
            .Include(c => c.CreatedByUser)
            .Include(c => c.Department)
            .FirstOrDefaultAsync(c => c.ClientId == clientId && c.DepartmentId == request.DepartmentId && c.CreatedByUserId == request.TargetUserId);

        if (conversation == null)
        {
            conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                ClientId = clientId,
                DepartmentId = request.DepartmentId,
                CreatedByUserId = request.TargetUserId,
                Status = ConversationStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Conversations.Add(conversation);
            await _dbContext.SaveChangesAsync();

            conversation.CreatedByUser = targetUser;
            conversation.Department = department;
        }

        var unreadCount = await _dbContext.Messages
            .Where(m => m.ConversationId == conversation.Id && m.SenderRole == UserRole.User && m.Status < MessageDeliveryStatus.Read)
            .CountAsync();

        var isOnline = await _presenceService.IsUserOnlineAsync(targetUser.Id);

        var summary = new ConversationSummaryDto(
            conversation.Id,
            conversation.DepartmentId,
            department.Name,
            conversation.CreatedByUserId,
            conversation.CreatedByUser.FullName,
            SanitizePreview(conversation.LastMessagePreview),
            conversation.LastMessageAt,
            unreadCount,
            conversation.Status,
            isOnline
        );

        return Ok(summary);
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
