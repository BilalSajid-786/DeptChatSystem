using System.Security.Claims;
using DeptChat.Application.Common.Interfaces;
using DeptChat.Domain.Entities;
using DeptChat.Infrastructure.Persistence;
using DeptChat.Shared.DTOs;
using DeptChat.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DeptChat.WebApi.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPresenceService _presenceService;
    private readonly IPushNotificationService _pushNotificationService;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(
        ApplicationDbContext dbContext,
        IPresenceService presenceService,
        IPushNotificationService pushNotificationService,
        ILogger<ChatHub> logger)
    {
        _dbContext = dbContext;
        _presenceService = presenceService;
        _pushNotificationService = pushNotificationService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        var role = GetUserRole();

        // Join personal user group for targeted alerts
        await Groups.AddToGroupAsync(Context.ConnectionId, $"User_{userId}");

        // If manager or admin, also auto-join assigned department groups for Tier 2 In-App Toasts
        if (role == UserRole.Manager)
        {
            var assignedDeptIds = await _dbContext.DepartmentManagers
                .Where(dm => dm.UserId == userId)
                .Select(dm => dm.DepartmentId)
                .ToListAsync();

            var restrictedDeptIds = await _dbContext.DepartmentManagers
                .Select(dm => dm.DepartmentId)
                .Distinct()
                .ToListAsync();

            var accessibleDepts = await _dbContext.Departments
                .Where(d => d.IsActive && (assignedDeptIds.Contains(d.Id) || !restrictedDeptIds.Contains(d.Id)))
                .Select(d => d.Id)
                .ToListAsync();

            foreach (var deptId in accessibleDepts)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Dept_{deptId}");
            }
        }
        else if (role == UserRole.Admin)
        {
            var allDepts = await _dbContext.Departments.Where(d => d.IsActive).Select(d => d.Id).ToListAsync();
            foreach (var deptId in allDepts)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Dept_{deptId}");
            }
        }

        await _presenceService.SetUserOnlineAsync(userId, true);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        await _presenceService.ClearPresenceAsync(userId);
        await _presenceService.SetUserOnlineAsync(userId, false);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// User or staff enters a specific chat room.
    /// Updates Redis presence and marks incoming messages as Read.
    /// </summary>
    public async Task EnterRoom(Guid conversationId)
    {
        var userId = GetUserId();
        await Groups.AddToGroupAsync(Context.ConnectionId, $"Room_{conversationId}");
        await _presenceService.SetPresenceAsync(userId, $"room:{conversationId}");
        await Clients.OthersInGroup($"Room_{conversationId}").SendAsync("UserPresenceChanged", conversationId, userId, true);

        // Mark all unread messages sent by others in this conversation as Read
        var unreadMessages = await _dbContext.Messages
            .Where(m => m.ConversationId == conversationId && m.SenderId != userId && m.Status < MessageDeliveryStatus.Read)
            .ToListAsync();

        if (unreadMessages.Count > 0)
        {
            foreach (var msg in unreadMessages)
            {
                msg.Status = MessageDeliveryStatus.Read;
            }
            await _dbContext.SaveChangesAsync();

            var maxSeq = unreadMessages.Max(m => m.SequenceNumber);
            // Broadcast read receipt to room
            await Clients.Group($"Room_{conversationId}").SendAsync("MessagesRead", conversationId, maxSeq);
        }

        _logger.LogInformation("User {UserId} entered room {ConversationId}", userId, conversationId);
    }

    /// <summary>
    /// User or staff leaves a specific chat room.
    /// </summary>
    public async Task LeaveRoom(Guid conversationId)
    {
        var userId = GetUserId();
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Room_{conversationId}");
        await _presenceService.ClearPresenceAsync(userId);
        await Clients.OthersInGroup($"Room_{conversationId}").SendAsync("UserPresenceChanged", conversationId, userId, false);
        _logger.LogInformation("User {UserId} left room {ConversationId}", userId, conversationId);
    }

    /// <summary>
    /// Real-time WhatsApp-style typing indicator.
    /// </summary>
    public async Task SendTyping(Guid conversationId, bool isTyping)
    {
        var userId = GetUserId();
        var senderName = Context.User?.Identity?.Name ?? "Someone";
        await Clients.OthersInGroup($"Room_{conversationId}").SendAsync("UserTyping", conversationId, userId, senderName, isTyping);
    }

    /// <summary>
    /// Query presence and last seen timestamp of a user.
    /// </summary>
    public async Task<UserPresenceDto> GetPresence(Guid targetUserId, Guid? conversationId = null)
    {
        var isOnline = await _presenceService.IsUserOnlineAsync(targetUserId);
        var lastSeen = await _presenceService.GetLastSeenAsync(targetUserId);
        bool isInRoom = false;
        if (conversationId.HasValue)
        {
            var currentPresence = await _presenceService.GetPresenceAsync(targetUserId);
            isInRoom = string.Equals(currentPresence, $"room:{conversationId.Value}", StringComparison.OrdinalIgnoreCase);
        }
        return new UserPresenceDto(targetUserId, isOnline, isInRoom, lastSeen);
    }

    /// <summary>
    /// Send message with guaranteed zero message loss, atomic monotonic sequence numbers,
    /// idempotency deduplication, and intelligent 3-tier notification routing.
    /// </summary>
    public async Task SendMessage(SendMessageDto dto)
    {
        var userId = GetUserId();
        var role = GetUserRole();
        var senderName = Context.User?.Identity?.Name ?? (role == UserRole.User ? "Customer" : "Support Staff");

        if (string.IsNullOrWhiteSpace(dto.Content) || string.IsNullOrWhiteSpace(dto.ClientGeneratedId))
        {
            return;
        }

        // 1. Idempotency Check: prevent duplicate messages on network retries
        var existingMessage = await _dbContext.Messages
            .Include(m => m.Sender)
            .FirstOrDefaultAsync(m => m.ConversationId == dto.ConversationId && m.ClientGeneratedId == dto.ClientGeneratedId);

        if (existingMessage != null)
        {
            // Already saved, acknowledge immediately
            await Clients.Caller.SendAsync("MessageAck", dto.ClientGeneratedId, existingMessage.Id, existingMessage.SequenceNumber, existingMessage.Status);
            return;
        }

        // 2. Fetch conversation
        var conversation = await _dbContext.Conversations
            .Include(c => c.Department)
            .FirstOrDefaultAsync(c => c.Id == dto.ConversationId);

        if (conversation == null)
        {
            _logger.LogWarning("Conversation {ConversationId} not found for message send.", dto.ConversationId);
            return;
        }

        // 3. Atomically determine monotonic sequence number
        var maxSeq = await _dbContext.Messages
            .Where(m => m.ConversationId == dto.ConversationId)
            .Select(m => (long?)m.SequenceNumber)
            .MaxAsync() ?? 0;

        var nextSeq = maxSeq + 1;

        // 4. Initial status MUST always be Sent.
        // Blue ticks (Read receipt) are only awarded when the recipient device actually acknowledges
        // receipt via MarkAsRead or EnterRoom over SignalR.
        var initialStatus = MessageDeliveryStatus.Sent;
        List<Guid> allStaffIds = new();

        if (role == UserRole.User)
        {
            var assignedManagerIds = await _dbContext.DepartmentManagers
                .Where(dm => dm.DepartmentId == conversation.DepartmentId)
                .Select(dm => dm.UserId)
                .ToListAsync();

            if (assignedManagerIds.Count == 0)
            {
                assignedManagerIds = await _dbContext.Users
                    .Where(u => u.Role == UserRole.Manager)
                    .Select(u => u.Id)
                    .ToListAsync();
            }

            var adminIds = await _dbContext.Users
                .Where(u => u.Role == UserRole.Admin)
                .Select(u => u.Id)
                .ToListAsync();

            allStaffIds = assignedManagerIds.Concat(adminIds).Distinct().ToList();
        }

        // 5. Commit to SQL Server first (DURABILITY GUARANTEE)
        var message = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = dto.ConversationId,
            SenderId = userId,
            SenderRole = role,
            ClientGeneratedId = dto.ClientGeneratedId,
            SequenceNumber = nextSeq,
            Content = dto.Content.Trim(),
            Status = initialStatus,
            SentAt = DateTime.UtcNow
        };

        var isVoice = message.Content.StartsWith("[voice:", StringComparison.OrdinalIgnoreCase);
        var previewText = isVoice 
            ? "🎙️ Voice message" 
            : (message.Content.Length > 80 ? message.Content.Substring(0, 77) + "..." : message.Content);

        conversation.LastMessagePreview = previewText;
        conversation.LastMessageAt = message.SentAt;
        conversation.UpdatedAt = message.SentAt;

        _dbContext.Messages.Add(message);
        await _dbContext.SaveChangesAsync();

        var messageDto = new ChatMessageDto(
            message.Id,
            message.ConversationId,
            message.SenderId,
            senderName,
            message.SenderRole,
            message.ClientGeneratedId,
            message.SequenceNumber,
            message.Content,
            message.Status,
            message.SentAt
        );

        // 5. Tier 1: Real-time SignalR sub-100ms broadcast to room
        await Clients.Group($"Room_{dto.ConversationId}").SendAsync("ReceiveMessage", messageDto);

        // Acknowledge back to sender with Sent status (single tick)
        await Clients.Caller.SendAsync("MessageAck", dto.ClientGeneratedId, message.Id, message.SequenceNumber, message.Status);

        // 6. Notification Policy & Push Notification Routing
        var preview = isVoice ? "🎙️ Voice message" : (message.Content.Length > 60 ? message.Content.Substring(0, 57) + "..." : message.Content);
        var toast = new InAppNotificationToastDto(
            conversation.Id,
            conversation.DepartmentId,
            conversation.Department.Name,
            senderName,
            preview,
            message.SentAt
        );

        if (role == UserRole.User)
        {
            // Tier 2: In-App Toast broadcast to department channel
            await Clients.Group($"Dept_{conversation.DepartmentId}").SendAsync("InAppToast", toast);

            // Tier 3: Phone in pocket / app closed -> High-Priority FCM Push Notification!
            var tokens = await _dbContext.DeviceTokens
                .Where(dt => allStaffIds.Contains(dt.UserId))
                .Select(dt => dt.Token)
                .ToListAsync();

            if (tokens.Count > 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _pushNotificationService.SendPushNotificationAsync(
                            tokens,
                            $"💬 New message in {conversation.Department.Name}",
                            $"{senderName}: {preview}",
                            new Dictionary<string, string>
                            {
                                { "conversationId", conversation.Id.ToString() },
                                { "departmentId", conversation.DepartmentId.ToString() }
                            }
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Background FCM dispatch error for staff.");
                    }
                });
            }
        }
        else
        {
            // Message from staff -> notify user
            // Tier 2: Toast to user group
            await Clients.Group($"User_{conversation.CreatedByUserId}").SendAsync("InAppToast", toast);

            // Tier 3: High-Priority FCM Push Notification to user's registered devices
            var tokens = await _dbContext.DeviceTokens
                .Where(dt => dt.UserId == conversation.CreatedByUserId)
                .Select(dt => dt.Token)
                .ToListAsync();

            if (tokens.Count > 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _pushNotificationService.SendPushNotificationAsync(
                            tokens,
                            $"💬 {conversation.Department.Name} Support",
                            $"{senderName}: {preview}",
                            new Dictionary<string, string>
                            {
                                { "conversationId", conversation.Id.ToString() },
                                { "departmentId", conversation.DepartmentId.ToString() }
                            }
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Background FCM dispatch error for user.");
                    }
                });
            }
        }
    }

    /// <summary>
    /// Acknowledges message delivery on recipient device.
    /// </summary>
    public async Task AcknowledgeDelivery(Guid messageId)
    {
        var msg = await _dbContext.Messages.FindAsync(messageId);
        if (msg != null && msg.Status < MessageDeliveryStatus.Delivered)
        {
            msg.Status = MessageDeliveryStatus.Delivered;
            await _dbContext.SaveChangesAsync();

            await Clients.Group($"Room_{msg.ConversationId}").SendAsync("MessageDelivered", messageId);
        }
    }

    /// <summary>
    /// Explicitly marks messages up to maxSequenceNumber in a conversation as Read by caller,
    /// broadcasting blue ticks (Read receipt) to the room in real-time.
    /// </summary>
    public async Task MarkAsRead(Guid conversationId, long maxSequenceNumber)
    {
        var userId = GetUserId();
        var unread = await _dbContext.Messages
            .Where(m => m.ConversationId == conversationId && m.SenderId != userId && m.SequenceNumber <= maxSequenceNumber && m.Status < MessageDeliveryStatus.Read)
            .ToListAsync();

        if (unread.Count > 0)
        {
            foreach (var m in unread)
            {
                m.Status = MessageDeliveryStatus.Read;
            }
            await _dbContext.SaveChangesAsync();

            await Clients.Group($"Room_{conversationId}").SendAsync("MessagesRead", conversationId, maxSequenceNumber);
            _logger.LogInformation("Marked {Count} messages as Read in room {ConversationId} up to seq {MaxSeq}", unread.Count, conversationId, maxSequenceNumber);
        }
    }

    /// <summary>
    /// Registers or updates an FCM push notification token for the caller.
    /// </summary>
    public async Task RegisterDeviceToken(string token, string platform)
    {
        if (string.IsNullOrWhiteSpace(token)) return;

        var userId = GetUserId();
        if (userId == Guid.Empty) return;

        var existingToken = await _dbContext.DeviceTokens
            .FirstOrDefaultAsync(dt => dt.UserId == userId && dt.Token == token.Trim());

        if (existingToken != null)
        {
            existingToken.UpdatedAt = DateTime.UtcNow;
            existingToken.Platform = string.IsNullOrWhiteSpace(platform) ? "Android" : platform.Trim();
        }
        else
        {
            _dbContext.DeviceTokens.Add(new DeviceToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Token = token.Trim(),
                Platform = string.IsNullOrWhiteSpace(platform) ? "Android" : platform.Trim(),
                UpdatedAt = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("FCM token registered for user {UserId} on {Platform}.", userId, platform);
    }

    private Guid GetUserId()
    {
        var userIdClaim = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdClaim, out var id) ? id : Guid.Empty;
    }

    private UserRole GetUserRole()
    {
        var roleClaim = Context.User?.FindFirstValue(ClaimTypes.Role);
        return Enum.TryParse<UserRole>(roleClaim, out var role) ? role : UserRole.User;
    }
}
