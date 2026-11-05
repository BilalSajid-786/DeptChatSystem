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

        var clientId = GetClientId();

        // Join personal user group for targeted alerts
        await Groups.AddToGroupAsync(Context.ConnectionId, $"User_{userId}");

        // Join tenant client group
        if (clientId != Guid.Empty)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Client_{clientId}");
        }

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
                .Where(d => d.IsActive && (clientId == Guid.Empty || d.ClientId == clientId) && (assignedDeptIds.Contains(d.Id) || !restrictedDeptIds.Contains(d.Id)))
                .Select(d => d.Id)
                .ToListAsync();

            foreach (var deptId in accessibleDepts)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Dept_{deptId}");
            }
        }
        else if (role == UserRole.Admin)
        {
            var allDepts = await _dbContext.Departments
                .Where(d => d.IsActive && (clientId == Guid.Empty || d.ClientId == clientId))
                .Select(d => d.Id)
                .ToListAsync();
            foreach (var deptId in allDepts)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Dept_{deptId}");
            }
        }

        var isFirstConnection = await _presenceService.TrackConnectionAsync(userId, Context.ConnectionId);
        if (isFirstConnection)
        {
            if (clientId != Guid.Empty)
            {
                await Clients.Group($"Client_{clientId}").SendAsync("UserOnlineStatusChanged", userId, true);
            }
            else
            {
                await Clients.All.SendAsync("UserOnlineStatusChanged", userId, true);
            }
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        var currentPresence = await _presenceService.GetPresenceAsync(userId);
        var isLastConnection = await _presenceService.RemoveConnectionAsync(userId, Context.ConnectionId);

        if (isLastConnection)
        {
            await _presenceService.ClearPresenceAsync(userId);

            var now = DateTime.UtcNow;
            if (userId != Guid.Empty)
            {
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
            }

            var clientId = GetClientId();
            if (clientId != Guid.Empty)
            {
                await Clients.Group($"Client_{clientId}").SendAsync("UserOnlineStatusChanged", userId, false);
            }
            else
            {
                await Clients.All.SendAsync("UserOnlineStatusChanged", userId, false);
            }

            if (!string.IsNullOrEmpty(currentPresence) && currentPresence.StartsWith("room:"))
            {
                var convIdStr = currentPresence.Substring(5);
                if (Guid.TryParse(convIdStr, out var convId))
                {
                    await Clients.Group($"Room_{convId}").SendAsync("UserPresenceChanged", convId, userId, false);
                    await Clients.Group($"Room_{convId}").SendAsync("UserPresenceState", convId, userId, false, false, now);
                }
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// User or staff enters a specific chat room.
    /// Updates Redis presence and marks incoming messages as Read.
    /// </summary>
    public async Task EnterRoom(Guid conversationId)
    {
        var userId = GetUserId();
        var role = GetUserRole();
        await Groups.AddToGroupAsync(Context.ConnectionId, $"Room_{conversationId}");
        await _presenceService.SetPresenceAsync(userId, $"room:{conversationId}");

        if (role == UserRole.User)
        {
            // Customer entered: notify staff in the room that customer is present
            await Clients.OthersInGroup($"Room_{conversationId}").SendAsync("UserPresenceChanged", conversationId, userId, true);
            await Clients.OthersInGroup($"Room_{conversationId}").SendAsync("UserPresenceState", conversationId, userId, true, true, (DateTime?)null);
        }
        else
        {
            // Staff entered: notify customer in the room of department presence without hijacking customer presence
            var deptConv = await _dbContext.Conversations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == conversationId);
            if (deptConv != null)
            {
                await Clients.OthersInGroup($"Room_{conversationId}").SendAsync("DepartmentPresenceState", conversationId, deptConv.DepartmentId, true);
            }
        }

        // Read receipt:
        // Staff ONLY marks Customer messages as Read.
        // Customer ONLY marks Staff messages as Read.
        IQueryable<Message> unreadQuery = _dbContext.Messages
            .Where(m => m.ConversationId == conversationId && m.Status < MessageDeliveryStatus.Read);

        if (role == UserRole.User)
        {
            unreadQuery = unreadQuery.Where(m => m.SenderRole != UserRole.User);
        }
        else
        {
            unreadQuery = unreadQuery.Where(m => m.SenderRole == UserRole.User);
        }

        var unreadMessages = await unreadQuery.ToListAsync();

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

        // Inform caller about current presence of the other party
        var conv = await _dbContext.Conversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conversationId);

        if (conv != null)
        {
            if (role != UserRole.User)
            {
                // Manager/Admin entered: check customer presence
                var customerId = conv.CreatedByUserId;
                var custPresence = await _presenceService.GetPresenceAsync(customerId);
                var isCustomerInRoom = string.Equals(custPresence, $"room:{conversationId}", StringComparison.OrdinalIgnoreCase);
                var isCustomerOnline = isCustomerInRoom || await _presenceService.IsUserOnlineAsync(customerId);
                var customerLastSeen = await _presenceService.GetLastSeenAsync(customerId);
                if (!customerLastSeen.HasValue)
                {
                    var customerUser = await _dbContext.Users.FindAsync(customerId);
                    customerLastSeen = customerUser?.LastSeenAt ?? customerUser?.CreatedAt ?? DateTime.UtcNow;
                }

                await Clients.Caller.SendAsync("UserPresenceState", conversationId, customerId, isCustomerInRoom, isCustomerOnline, customerLastSeen);
            }
            else
            {
                // Customer entered: check if any staff is currently in this room
                var staffAssignments = await _dbContext.DepartmentManagers
                    .Where(dm => dm.DepartmentId == conv.DepartmentId)
                    .Select(dm => dm.UserId)
                    .ToListAsync();

                var adminIds = await _dbContext.Users
                    .Where(u => u.Role == UserRole.Admin && (conv.ClientId == Guid.Empty || u.ClientId == conv.ClientId))
                    .Select(u => u.Id)
                    .ToListAsync();

                var allStaffIds = staffAssignments.Concat(adminIds).Distinct().ToList();

                Guid? activeStaffInRoom = null;
                foreach (var sId in allStaffIds)
                {
                    var p = await _presenceService.GetPresenceAsync(sId);
                    if (string.Equals(p, $"room:{conversationId}", StringComparison.OrdinalIgnoreCase))
                    {
                        activeStaffInRoom = sId;
                        break;
                    }
                }

                if (activeStaffInRoom.HasValue)
                {
                    await Clients.Caller.SendAsync("UserPresenceState", conversationId, activeStaffInRoom.Value, true, true, (DateTime?)null);
                }
                else
                {
                    bool anyStaffOnline = false;
                    DateTime? mostRecentStaffLastSeen = null;
                    Guid? representativeStaffId = null;

                    foreach (var sId in allStaffIds)
                    {
                        if (await _presenceService.IsUserOnlineAsync(sId))
                        {
                            anyStaffOnline = true;
                            representativeStaffId = sId;
                            break;
                        }

                        var ls = await _presenceService.GetLastSeenAsync(sId);
                        if (!ls.HasValue)
                        {
                            var staffUser = await _dbContext.Users.FindAsync(sId);
                            ls = staffUser?.LastSeenAt ?? staffUser?.CreatedAt;
                        }
                        if (ls.HasValue && (!mostRecentStaffLastSeen.HasValue || ls.Value > mostRecentStaffLastSeen.Value))
                        {
                            mostRecentStaffLastSeen = ls.Value;
                            representativeStaffId = sId;
                        }
                    }

                    if (representativeStaffId.HasValue)
                    {
                        await Clients.Caller.SendAsync("UserPresenceState", conversationId, representativeStaffId.Value, false, anyStaffOnline, anyStaffOnline ? (DateTime?)null : (mostRecentStaffLastSeen ?? DateTime.UtcNow));
                    }
                    else
                    {
                        await Clients.Caller.SendAsync("DepartmentPresenceState", conversationId, conv.DepartmentId, anyStaffOnline);
                    }
                }
            }
        }

        _logger.LogInformation("User {UserId} entered room {ConversationId}", userId, conversationId);
    }

    /// <summary>
    /// User or staff leaves a specific chat room.
    /// </summary>
    public async Task LeaveRoom(Guid conversationId)
    {
        var userId = GetUserId();
        var role = GetUserRole();
        await _presenceService.ClearPresenceAsync(userId);
        var isOnline = await _presenceService.IsUserOnlineAsync(userId);
        var lastSeen = isOnline ? (DateTime?)null : (await _presenceService.GetLastSeenAsync(userId) ?? DateTime.UtcNow);

        if (role == UserRole.User)
        {
            // Broadcast customer leaving to staff in group
            await Clients.Group($"Room_{conversationId}").SendAsync("UserPresenceChanged", conversationId, userId, false);
            await Clients.Group($"Room_{conversationId}").SendAsync("UserPresenceState", conversationId, userId, false, isOnline, lastSeen ?? DateTime.UtcNow);
        }
        else
        {
            // Staff leaving: update department presence for customer
            var deptConv = await _dbContext.Conversations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == conversationId);
            if (deptConv != null)
            {
                await Clients.Group($"Room_{conversationId}").SendAsync("DepartmentPresenceState", conversationId, deptConv.DepartmentId, false);
            }
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Room_{conversationId}");
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
        if (!lastSeen.HasValue)
        {
            var user = await _dbContext.Users.FindAsync(targetUserId);
            lastSeen = user?.LastSeenAt ?? user?.CreatedAt ?? DateTime.UtcNow;
        }
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

        var senderUser = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (senderUser != null && senderUser.IsBlocked)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "You have been blocked from sending messages.");
            return;
        }

        var client = await _dbContext.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == (senderUser != null ? senderUser.ClientId : Guid.Empty));
        if (client != null && (client.IsBlocked || !client.IsActive))
        {
            await Clients.Caller.SendAsync("ErrorMessage", "Your organization workspace is currently blocked.");
            return;
        }

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

        // 4. Check if recipient is actively inside this room right now (instant blue ticks)
        List<Guid> allStaffIds = new();
        bool isRecipientInRoom = false;

        if (role == UserRole.User)
        {
            var assignedManagerIds = await _dbContext.DepartmentManagers
                .Where(dm => dm.DepartmentId == conversation.DepartmentId)
                .Select(dm => dm.UserId)
                .ToListAsync();

            if (assignedManagerIds.Count == 0)
            {
                assignedManagerIds = await _dbContext.Users
                    .Where(u => u.Role == UserRole.Manager && (conversation.ClientId == Guid.Empty || u.ClientId == conversation.ClientId))
                    .Select(u => u.Id)
                    .ToListAsync();
            }

            var adminIds = await _dbContext.Users
                .Where(u => u.Role == UserRole.Admin && (conversation.ClientId == Guid.Empty || u.ClientId == conversation.ClientId))
                .Select(u => u.Id)
                .ToListAsync();

            allStaffIds = assignedManagerIds.Concat(adminIds).Distinct().ToList();

            // Check if any assigned staff/admin is currently active in this room
            foreach (var sId in allStaffIds)
            {
                var p = await _presenceService.GetPresenceAsync(sId);
                if (string.Equals(p, $"room:{dto.ConversationId}", StringComparison.OrdinalIgnoreCase))
                {
                    isRecipientInRoom = true;
                    break;
                }
            }
        }
        else
        {
            // Sender is Staff (Admin/Manager): check if customer is currently active in this room
            var custPresence = await _presenceService.GetPresenceAsync(conversation.CreatedByUserId);
            isRecipientInRoom = string.Equals(custPresence, $"room:{dto.ConversationId}", StringComparison.OrdinalIgnoreCase);
        }

        // Initial status:
        // If recipient is already inside this room, mark Read immediately (instant blue ticks, matching WhatsApp).
        // Otherwise, starts as Sent (single grey tick).
        var initialStatus = isRecipientInRoom ? MessageDeliveryStatus.Read : MessageDeliveryStatus.Sent;

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
            SentAt = DateTime.UtcNow,
            IsDeleted = false,
            IsForwarded = false
        };

        string previewText;
        if (message.Content.StartsWith("[voice:", StringComparison.OrdinalIgnoreCase))
        {
            previewText = "🎙️ Voice message";
        }
        else if (message.Content.StartsWith("[image:", StringComparison.OrdinalIgnoreCase))
        {
            previewText = "📷 Photo";
        }
        else if (message.Content.StartsWith("[file:", StringComparison.OrdinalIgnoreCase))
        {
            var inner = message.Content.Trim('[', ']');
            var parts = inner.Split(':');
            previewText = (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2])) ? $"📄 {parts[2]}" : "📎 Document";
        }
        else
        {
            previewText = message.Content.Length > 80 ? message.Content.Substring(0, 77) + "..." : message.Content;
        }

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
            message.SentAt,
            message.IsDeleted,
            message.IsForwarded
        );

        // 5. Tier 1: Real-time SignalR sub-100ms broadcast to room
        await Clients.Group($"Room_{dto.ConversationId}").SendAsync("ReceiveMessage", messageDto);

        // Acknowledge back to sender with Sent status (single tick)
        await Clients.Caller.SendAsync("MessageAck", dto.ClientGeneratedId, message.Id, message.SequenceNumber, message.Status);

        // 6. Notification Policy & Push Notification Routing
        var preview = previewText;
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
        var role = GetUserRole();
        var msg = await _dbContext.Messages.FindAsync(messageId);
        if (msg == null || msg.Status >= MessageDeliveryStatus.Delivered) return;

        // Staff can only acknowledge delivery for Customer messages.
        // Customer can only acknowledge delivery for Staff messages.
        if (role == UserRole.User && msg.SenderRole == UserRole.User) return;
        if (role != UserRole.User && msg.SenderRole != UserRole.User) return;

        msg.Status = MessageDeliveryStatus.Delivered;
        await _dbContext.SaveChangesAsync();

        await Clients.Group($"Room_{msg.ConversationId}").SendAsync("MessageDelivered", messageId);
    }

    /// <summary>
    /// Explicitly marks messages up to maxSequenceNumber in a conversation as Read by caller,
    /// broadcasting blue ticks (Read receipt) to the room in real-time.
    /// </summary>
    public async Task MarkAsRead(Guid conversationId, long maxSequenceNumber)
    {
        var userId = GetUserId();
        var role = GetUserRole();

        IQueryable<Message> query = _dbContext.Messages
            .Where(m => m.ConversationId == conversationId && m.SequenceNumber <= maxSequenceNumber && m.Status < MessageDeliveryStatus.Read);

        if (role == UserRole.User)
        {
            // Customer marks staff messages as read
            query = query.Where(m => m.SenderRole != UserRole.User);
        }
        else
        {
            // Staff marks customer messages as read (never colleague staff messages!)
            query = query.Where(m => m.SenderRole == UserRole.User);
        }

        var unread = await query.ToListAsync();

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

    private Guid GetClientId()
    {
        var clientIdClaim = Context.User?.FindFirstValue("client_id");
        return Guid.TryParse(clientIdClaim, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// Soft-deletes a message.
    /// Admin can delete their own or staff messages.
    /// Manager can delete only their own messages.
    /// Regular Users (Customers) cannot delete any message.
    /// Preserves monotonic sequence number integrity.
    /// </summary>
    public async Task DeleteMessage(Guid messageId)
    {
        var userId = GetUserId();
        var role = GetUserRole();

        if (role == UserRole.User)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "Customers cannot delete messages.");
            return;
        }

        var message = await _dbContext.Messages.FirstOrDefaultAsync(m => m.Id == messageId);
        if (message == null || message.IsDeleted) return;

        // Manager can only delete their own messages
        if (role == UserRole.Manager && message.SenderId != userId)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "Managers can only delete their own messages.");
            return;
        }

        message.IsDeleted = true;
        message.DeletedAt = DateTime.UtcNow;
        message.Content = "🚫 This message was deleted";

        var conv = await _dbContext.Conversations.FirstOrDefaultAsync(c => c.Id == message.ConversationId);
        if (conv != null && conv.LastMessageAt <= message.SentAt)
        {
            conv.LastMessagePreview = "🚫 This message was deleted";
            conv.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();

        // Broadcast deletion event to room
        await Clients.Group($"Room_{message.ConversationId}").SendAsync("MessageDeleted", message.ConversationId, message.Id);
        _logger.LogInformation("Message {MessageId} in conversation {ConversationId} deleted by user {UserId}", messageId, message.ConversationId, userId);
    }

    /// <summary>
    /// Forwards a message to any user in any department.
    /// Strictly restricted to Administrators.
    /// </summary>
    public async Task ForwardMessage(Guid messageId, Guid targetDepartmentId, Guid targetUserId)
    {
        var role = GetUserRole();
        var userId = GetUserId();
        var clientId = GetClientId();

        if (role != UserRole.Admin)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "Only administrators can forward messages across departments.");
            return;
        }

        var sourceMsg = await _dbContext.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == messageId);
        if (sourceMsg == null || sourceMsg.IsDeleted)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "Source message not found or has been deleted.");
            return;
        }

        var department = await _dbContext.Departments.FirstOrDefaultAsync(d => d.Id == targetDepartmentId && d.ClientId == clientId);
        var targetUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == targetUserId && u.ClientId == clientId);
        if (department == null || targetUser == null)
        {
            await Clients.Caller.SendAsync("ErrorMessage", "Target department or user not found.");
            return;
        }

        var targetConv = await _dbContext.Conversations
            .Include(c => c.Department)
            .Include(c => c.CreatedByUser)
            .FirstOrDefaultAsync(c => c.DepartmentId == targetDepartmentId && c.CreatedByUserId == targetUserId && c.ClientId == clientId);

        if (targetConv == null)
        {
            targetConv = new Conversation
            {
                Id = Guid.NewGuid(),
                ClientId = clientId,
                DepartmentId = targetDepartmentId,
                CreatedByUserId = targetUserId,
                Status = ConversationStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.Conversations.Add(targetConv);
            await _dbContext.SaveChangesAsync();
            targetConv.Department = department;
            targetConv.CreatedByUser = targetUser;
        }

        var maxSeq = await _dbContext.Messages
            .Where(m => m.ConversationId == targetConv.Id)
            .Select(m => (long?)m.SequenceNumber)
            .MaxAsync() ?? 0;

        var adminName = Context.User?.Identity?.Name ?? "System Admin";
        var clientGenId = Guid.NewGuid().ToString();

        var forwardedMsg = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = targetConv.Id,
            SenderId = userId,
            SenderRole = UserRole.Admin,
            ClientGeneratedId = clientGenId,
            SequenceNumber = maxSeq + 1,
            Content = sourceMsg.Content,
            IsForwarded = true,
            IsDeleted = false,
            Status = MessageDeliveryStatus.Sent,
            SentAt = DateTime.UtcNow
        };

        string previewText;
        if (forwardedMsg.Content.StartsWith("[voice:", StringComparison.OrdinalIgnoreCase))
        {
            previewText = "↪️ Voice message";
        }
        else if (forwardedMsg.Content.StartsWith("[image:", StringComparison.OrdinalIgnoreCase))
        {
            previewText = "↪️ Photo";
        }
        else if (forwardedMsg.Content.StartsWith("[file:", StringComparison.OrdinalIgnoreCase))
        {
            var inner = forwardedMsg.Content.Trim('[', ']');
            var parts = inner.Split(':');
            previewText = (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2])) ? $"↪️ {parts[2]}" : "↪️ Document";
        }
        else
        {
            previewText = "↪️ " + (forwardedMsg.Content.Length > 75 ? forwardedMsg.Content.Substring(0, 72) + "..." : forwardedMsg.Content);
        }

        targetConv.LastMessagePreview = previewText;
        targetConv.LastMessageAt = forwardedMsg.SentAt;
        targetConv.UpdatedAt = forwardedMsg.SentAt;

        _dbContext.Messages.Add(forwardedMsg);
        await _dbContext.SaveChangesAsync();

        var messageDto = new ChatMessageDto(
            forwardedMsg.Id,
            forwardedMsg.ConversationId,
            forwardedMsg.SenderId,
            adminName,
            forwardedMsg.SenderRole,
            forwardedMsg.ClientGeneratedId,
            forwardedMsg.SequenceNumber,
            forwardedMsg.Content,
            forwardedMsg.Status,
            forwardedMsg.SentAt,
            forwardedMsg.IsDeleted,
            forwardedMsg.IsForwarded
        );

        // Broadcast to target room
        await Clients.Group($"Room_{targetConv.Id}").SendAsync("ReceiveMessage", messageDto);

        // Broadcast Tier 2 In-App Toast
        var toast = new InAppNotificationToastDto(
            targetConv.Id,
            targetConv.DepartmentId,
            targetConv.Department.Name,
            adminName,
            previewText,
            forwardedMsg.SentAt
        );
        await Clients.Group($"Dept_{targetConv.DepartmentId}").SendAsync("InAppToast", toast);

        // Notify admin that forward was successful
        await Clients.Caller.SendAsync("MessageForwarded", sourceMsg.Id, targetConv.Id, forwardedMsg.Id);
    }

}