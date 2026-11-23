using System.Security.Claims;
using DeptChat.Application.Common.Interfaces;
using DeptChat.Domain.Entities;
using DeptChat.Infrastructure.Persistence;
using DeptChat.Shared.DTOs;
using DeptChat.Shared.Enums;
using DeptChat.WebApi.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DeptChat.WebApi.Controllers;

[Authorize]
[ApiController]
[Route("api/conversations")]
public class ConversationsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPresenceService _presenceService;
    private readonly IHubContext<ChatHub> _hubContext;

    // Personal pins per person per chat (like WhatsApp).
    private const int MaxPinsPerChat = 3;

    public ConversationsController(ApplicationDbContext dbContext, IPresenceService presenceService, IHubContext<ChatHub> hubContext)
    {
        _dbContext = dbContext;
        _presenceService = presenceService;
        _hubContext = hubContext;
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

            // Cleared messages (for everyone or by this user) are not counted and not previewed.
            var clearedUpTo = await GetClearedUpToSequenceAsync(conversation, userId);

            var unreadCount = await _dbContext.Messages
                .Where(m => m.ConversationId == conversation.Id && m.SequenceNumber > clearedUpTo && (m.ExpiresAt == null || m.ExpiresAt > DateTime.UtcNow) && m.SenderId != userId && m.Status < MessageDeliveryStatus.Read)
                .CountAsync();

            var hasVisibleMessages = await _dbContext.Messages
                .AnyAsync(m => m.ConversationId == conversation.Id && m.SequenceNumber > clearedUpTo && (m.ExpiresAt == null || m.ExpiresAt > DateTime.UtcNow));

            var isUserOnline = await _presenceService.IsUserOnlineAsync(userId);

            var summary = new ConversationSummaryDto(
                conversation.Id,
                conversation.DepartmentId,
                department.Name,
                conversation.CreatedByUserId,
                conversation.CreatedByUser.FullName,
                SanitizePreview(hasVisibleMessages ? conversation.LastMessagePreview : null),
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
                .OrderByDescending(c => c.LastMessageAt ?? c.UpdatedAt)
                .ToListAsync();

            // My pinned chats first (latest pin on top); the rest keep the latest-message order.
            var myPins = await _dbContext.ConversationPins
                .Where(p => p.UserId == userId && _dbContext.Conversations.Any(c => c.Id == p.ConversationId && c.DepartmentId == departmentId))
                .ToDictionaryAsync(p => p.ConversationId, p => p.PinnedAt);
            if (myPins.Count > 0)
            {
                conversations = conversations
                    .OrderByDescending(c => myPins.ContainsKey(c.Id))
                    .ThenByDescending(c => myPins.TryGetValue(c.Id, out var pinnedAt) ? pinnedAt : DateTime.MinValue)
                    .ToList();
            }

            var convIds = conversations.Select(c => c.Id).ToList();
            var unreadCounts = await _dbContext.Messages
                .Where(m => convIds.Contains(m.ConversationId) && m.SequenceNumber > m.Conversation.ClearedUpToSequence && (m.ExpiresAt == null || m.ExpiresAt > DateTime.UtcNow)
                            && m.SenderRole == UserRole.User && m.Status < MessageDeliveryStatus.Read
                            && !_dbContext.ConversationUserClears.Any(c => c.ConversationId == m.ConversationId && c.UserId == userId && c.ClearedUpToSequence >= m.SequenceNumber))
                .GroupBy(m => m.ConversationId)
                .Select(g => new { ConversationId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ConversationId, x => x.Count);

            // Chats I cleared only for myself: no preview unless something newer is visible.
            var myClears = await _dbContext.ConversationUserClears
                .Where(c => c.UserId == userId && convIds.Contains(c.ConversationId))
                .ToDictionaryAsync(c => c.ConversationId, c => c.ClearedUpToSequence);
            var hiddenPreview = new HashSet<Guid>();
            foreach (var (convId, mySeq) in myClears)
            {
                var visibleAfter = await _dbContext.Messages.AnyAsync(m => m.ConversationId == convId && m.SequenceNumber > mySeq
                                                                          && (m.ExpiresAt == null || m.ExpiresAt > DateTime.UtcNow));
                if (!visibleAfter) hiddenPreview.Add(convId);
            }

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
                    SanitizePreview(hiddenPreview.Contains(conv.Id) ? null : conv.LastMessagePreview),
                    conv.LastMessageAt,
                    unreadCounts.GetValueOrDefault(conv.Id, 0),
                    conv.Status,
                    isOnline,
                    conv.CreatedByUser?.IsBlocked ?? false,
                    myPins.ContainsKey(conv.Id)
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

        var clearedUpTo = await GetClearedUpToSequenceAsync(conv, userId);

        var unreadCount = await _dbContext.Messages
            .Where(m => m.ConversationId == conv.Id && m.SequenceNumber > clearedUpTo && (m.ExpiresAt == null || m.ExpiresAt > DateTime.UtcNow) && m.SenderId != userId && m.Status < MessageDeliveryStatus.Read)
            .CountAsync();

        var hasVisibleMessages = await _dbContext.Messages
            .AnyAsync(m => m.ConversationId == conv.Id && m.SequenceNumber > clearedUpTo && (m.ExpiresAt == null || m.ExpiresAt > DateTime.UtcNow));

        var isOnline = await _presenceService.IsUserOnlineAsync(conv.CreatedByUserId);

        return Ok(new ConversationSummaryDto(
            conv.Id,
            conv.DepartmentId,
            conv.Department.Name,
            conv.CreatedByUserId,
            conv.CreatedByUser.FullName,
            hasVisibleMessages ? conv.LastMessagePreview : null,
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

        // Messages cleared for everyone or by this user are never returned.
        var clearedUpTo = await GetClearedUpToSequenceAsync(conv, userId);
        var pinnedIds = await GetPinnedMessageIdsAsync(id, userId);

        IQueryable<Message> query = _dbContext.Messages
            .Include(m => m.Sender)
            .Where(m => m.ConversationId == id && m.SequenceNumber > clearedUpTo && (m.ExpiresAt == null || m.ExpiresAt > DateTime.UtcNow));

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

        var messageDtos = pagedList.Select(m => ToMessageDto(m, pinnedIds)).ToList();

        long? earliestSeq = messageDtos.Count > 0 ? messageDtos.Min(m => m.SequenceNumber) : null;
        long? latestSeq = messageDtos.Count > 0 ? messageDtos.Max(m => m.SequenceNumber) : null;

        return Ok(new PaginatedMessagesDto(id, earliestSeq, latestSeq, hasMore, messageDtos, clearedUpTo));
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

        // Messages cleared for everyone or by this user are never returned; the app removes
        // its local copies up to ClearedUpToSequence (also when it missed the live event).
        var clearedUpTo = await GetClearedUpToSequenceAsync(conv, userId);
        var pinnedIds = await GetPinnedMessageIdsAsync(id, userId);
        var fromSeq = Math.Max(sinceSequenceNumber, clearedUpTo);

        var messages = await _dbContext.Messages
            .Include(m => m.Sender)
            .Where(m => m.ConversationId == id && m.SequenceNumber > fromSeq && (m.ExpiresAt == null || m.ExpiresAt > DateTime.UtcNow))
            .OrderBy(m => m.SequenceNumber)
            .ToListAsync();

        var serverMaxSeq = await _dbContext.Messages
            .Where(m => m.ConversationId == id)
            .MaxAsync(m => (long?)m.SequenceNumber) ?? 0;

        var lastSeq = messages.Count > 0 ? messages.Max(m => m.SequenceNumber) : serverMaxSeq;

        var messageDtos = messages.Select(m => ToMessageDto(m, pinnedIds)).ToList();

        return Ok(new ConversationSyncResponseDto(id, lastSeq, messageDtos, clearedUpTo));
    }

    // =====================================================================
    // Personal pins
    // =====================================================================

    /// <summary>The caller's own pinned messages in this chat (newest pin first). Nobody else sees them.</summary>
    [HttpGet("{id}/pins")]
    public async Task<IActionResult> GetPins(Guid id)
    {
        var userId = GetUserId();
        var conv = await GetAccessibleConversationAsync(id);
        if (conv == null) return NotFound(new { message = "Conversation not found." });

        var clearedUpTo = await GetClearedUpToSequenceAsync(conv, userId);

        var pins = await _dbContext.MessagePins
            .Where(p => p.UserId == userId && p.ConversationId == id)
            .OrderByDescending(p => p.PinnedAt)
            .ToListAsync();
        var messageIds = pins.Select(p => p.MessageId).ToList();

        var messages = await _dbContext.Messages
            .Include(m => m.Sender)
            .Where(m => messageIds.Contains(m.Id) && m.ConversationId == id && !m.IsDeleted && m.SequenceNumber > clearedUpTo && (m.ExpiresAt == null || m.ExpiresAt > DateTime.UtcNow))
            .ToListAsync();

        var pinnedIds = messageIds.ToHashSet();
        var result = pins
            .Select(p => messages.FirstOrDefault(m => m.Id == p.MessageId))
            .Where(m => m != null)
            .Select(m => ToMessageDto(m!, pinnedIds))
            .ToList();

        return Ok(result);
    }

    // =====================================================================
    // Disappearing messages (one setting for the whole company)
    // =====================================================================

    // Off, 24 hours, 7, 14, 30 and 90 days.
    private static readonly int[] AllowedDisappearingSeconds = { 0, 86400, 604800, 1209600, 2592000, 7776000 };

    /// <summary>Everyone: the company's current disappearing-messages time (0 = off).</summary>
    [HttpGet("disappearing-messages")]
    public async Task<IActionResult> GetDisappearingMessages()
    {
        var clientId = GetClientId();
        var seconds = await _dbContext.Clients.Where(c => c.Id == clientId)
            .Select(c => (int?)c.DisappearingMessagesSeconds).FirstOrDefaultAsync();
        if (seconds == null) return NotFound(new { message = "Company not found." });
        return Ok(new DisappearingMessagesSettingDto(seconds.Value));
    }

    /// <summary>Admins only: changes it for every chat in the company. Only messages sent from now on use the new time.</summary>
    [HttpPut("disappearing-messages")]
    public async Task<IActionResult> SetDisappearingMessages([FromBody] DisappearingMessagesSettingDto dto)
    {
        if (GetUserRole() != UserRole.Admin) return Forbid();
        if (dto == null || !AllowedDisappearingSeconds.Contains(dto.Seconds))
        {
            return BadRequest(new { message = "Choose Off, 24 hours, 7, 14, 30 or 90 days." });
        }

        var clientId = GetClientId();
        var client = await _dbContext.Clients.FirstOrDefaultAsync(c => c.Id == clientId);
        if (client == null) return NotFound(new { message = "Company not found." });

        client.DisappearingMessagesSeconds = dto.Seconds;
        await _dbContext.SaveChangesAsync();

        // Every open app and web page of this company updates its chat banner.
        await _hubContext.Clients.Group($"Client_{clientId}").SendAsync("DisappearingMessagesChanged", dto.Seconds);
        return Ok(new DisappearingMessagesSettingDto(dto.Seconds));
    }

    /// <summary>Pins a message for the caller only (up to 3 per chat).</summary>
    [HttpPost("{id}/messages/{messageId}/pin")]
    public async Task<IActionResult> PinMessage(Guid id, Guid messageId)
    {
        var userId = GetUserId();
        var conv = await GetAccessibleConversationAsync(id);
        if (conv == null) return NotFound(new { message = "Conversation not found." });

        var clearedUpTo = await GetClearedUpToSequenceAsync(conv, userId);
        var message = await _dbContext.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ConversationId == id && !m.IsDeleted && m.SequenceNumber > clearedUpTo && (m.ExpiresAt == null || m.ExpiresAt > DateTime.UtcNow));
        if (message == null) return NotFound(new { message = "Message not found." });

        if (await _dbContext.MessagePins.AnyAsync(p => p.UserId == userId && p.MessageId == messageId))
        {
            return Ok(new { message = "Message pinned." });
        }

        var pinCount = await _dbContext.MessagePins.CountAsync(p => p.UserId == userId && p.ConversationId == id);
        if (pinCount >= MaxPinsPerChat)
        {
            return Conflict(new { message = $"You can pin up to {MaxPinsPerChat} messages in a chat. Unpin one first." });
        }

        _dbContext.MessagePins.Add(new MessagePin { UserId = userId, ConversationId = id, MessageId = messageId });
        await _dbContext.SaveChangesAsync();

        // The caller's other devices refresh their pins.
        await _hubContext.Clients.Group($"User_{userId}").SendAsync("PinsChanged", id);
        return Ok(new { message = "Message pinned." });
    }

    /// <summary>Removes the caller's own pin.</summary>
    [HttpDelete("{id}/messages/{messageId}/pin")]
    public async Task<IActionResult> UnpinMessage(Guid id, Guid messageId)
    {
        var userId = GetUserId();
        var conv = await GetAccessibleConversationAsync(id);
        if (conv == null) return NotFound(new { message = "Conversation not found." });

        var pin = await _dbContext.MessagePins.FirstOrDefaultAsync(p => p.UserId == userId && p.ConversationId == id && p.MessageId == messageId);
        if (pin != null)
        {
            _dbContext.MessagePins.Remove(pin);
            await _dbContext.SaveChangesAsync();
            await _hubContext.Clients.Group($"User_{userId}").SendAsync("PinsChanged", id);
        }

        return Ok(new { message = "Message unpinned." });
    }

    /// <summary>Admins/managers: pins a customer chat at the top of the caller's own department chat list (no limit).</summary>
    [HttpPost("{id}/pin")]
    public async Task<IActionResult> PinConversation(Guid id)
    {
        if (GetUserRole() == UserRole.User) return Forbid();
        var userId = GetUserId();
        var conv = await GetAccessibleConversationAsync(id);
        if (conv == null) return NotFound(new { message = "Conversation not found." });

        if (!await _dbContext.ConversationPins.AnyAsync(p => p.UserId == userId && p.ConversationId == id))
        {
            _dbContext.ConversationPins.Add(new ConversationPin { UserId = userId, ConversationId = id });
            await _dbContext.SaveChangesAsync();
        }

        // The caller's other devices (and web) reorder their list.
        await _hubContext.Clients.Group($"User_{userId}").SendAsync("ConversationPinsChanged", conv.DepartmentId);
        return Ok(new { message = "Chat pinned." });
    }

    /// <summary>Admins/managers: removes the caller's own chat pin.</summary>
    [HttpDelete("{id}/pin")]
    public async Task<IActionResult> UnpinConversation(Guid id)
    {
        if (GetUserRole() == UserRole.User) return Forbid();
        var userId = GetUserId();
        var conv = await GetAccessibleConversationAsync(id);
        if (conv == null) return NotFound(new { message = "Conversation not found." });

        var pin = await _dbContext.ConversationPins.FirstOrDefaultAsync(p => p.UserId == userId && p.ConversationId == id);
        if (pin != null)
        {
            _dbContext.ConversationPins.Remove(pin);
            await _dbContext.SaveChangesAsync();
        }

        await _hubContext.Clients.Group($"User_{userId}").SendAsync("ConversationPinsChanged", conv.DepartmentId);
        return Ok(new { message = "Chat unpinned." });
    }

    // =====================================================================
    // Clear chat
    // =====================================================================

    /// <summary>
    /// Clears the chat.
    /// - User: only for themselves (their own chat with the department); staff still see everything.
    /// - Admin / Manager (with access to the department): for themselves (?forEveryone=false) or for
    ///   everyone (default: the user, admins and managers).
    /// Messages stay in the database (sequence numbers keep increasing) but are never shown again.
    /// </summary>
    [HttpPost("{id}/clear")]
    public async Task<IActionResult> ClearChat(Guid id, [FromQuery] bool? forEveryone = null)
    {
        var userId = GetUserId();
        var role = GetUserRole();
        var conv = await GetAccessibleConversationAsync(id);
        if (conv == null) return NotFound(new { message = "Conversation not found." });

        var maxSeq = await _dbContext.Messages
            .Where(m => m.ConversationId == id)
            .MaxAsync(m => (long?)m.SequenceNumber) ?? 0;

        // Users always clear only for themselves; staff choose (for everyone when not said).
        if (role == UserRole.User || forEveryone == false)
        {
            var clear = await _dbContext.ConversationUserClears.FirstOrDefaultAsync(c => c.ConversationId == id && c.UserId == userId);
            if (clear == null)
            {
                clear = new ConversationUserClear { ConversationId = id, UserId = userId };
                _dbContext.ConversationUserClears.Add(clear);
            }
            clear.ClearedUpToSequence = Math.Max(clear.ClearedUpToSequence, maxSeq);
            clear.ClearedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            // The user's own pins on cleared messages go too.
            await _dbContext.MessagePins
                .Where(p => p.UserId == userId && p.ConversationId == id
                            && _dbContext.Messages.Any(m => m.Id == p.MessageId && m.SequenceNumber <= clear.ClearedUpToSequence))
                .ExecuteDeleteAsync();

            // Only the caller's own devices clear (nobody else is told: nothing changes for them).
            await _hubContext.Clients.Group($"User_{userId}").SendAsync("ConversationCleared", id, clear.ClearedUpToSequence, false);
            return Ok(new ClearChatResultDto(id, clear.ClearedUpToSequence, false));
        }

        conv.ClearedUpToSequence = Math.Max(conv.ClearedUpToSequence, maxSeq);
        conv.ClearedAt = DateTime.UtcNow;
        conv.ClearedByUserId = userId;
        conv.LastMessagePreview = null;
        conv.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Everyone's pins on cleared messages go too.
        var clearedSeq = conv.ClearedUpToSequence;
        await _dbContext.MessagePins
            .Where(p => p.ConversationId == id
                        && _dbContext.Messages.Any(m => m.Id == p.MessageId && m.SequenceNumber <= clearedSeq))
            .ExecuteDeleteAsync();

        // Open chats (Room), the user's devices, and staff lists of this department.
        await _hubContext.Clients.Groups($"Room_{id}", $"User_{conv.CreatedByUserId}", $"Dept_{conv.DepartmentId}")
            .SendAsync("ConversationCleared", id, clearedSeq, true);
        return Ok(new ClearChatResultDto(id, clearedSeq, true));
    }

    /// <summary>
    /// Loads the conversation if the caller may open it (same rule as GetMessages):
    /// own tenant; a user only their own chat; a manager only departments they may access.
    /// </summary>
    private async Task<Conversation?> GetAccessibleConversationAsync(Guid id)
    {
        var userId = GetUserId();
        var role = GetUserRole();
        var clientId = GetClientId();

        var conv = await _dbContext.Conversations.FirstOrDefaultAsync(c => c.Id == id && c.ClientId == clientId);
        if (conv == null) return null;

        if (role == UserRole.User && conv.CreatedByUserId != userId) return null;

        if (role == UserRole.Manager)
        {
            var hasExplicitManagers = await _dbContext.DepartmentManagers
                .AnyAsync(dm => dm.DepartmentId == conv.DepartmentId);
            if (hasExplicitManagers)
            {
                var isAssigned = await _dbContext.DepartmentManagers
                    .AnyAsync(dm => dm.DepartmentId == conv.DepartmentId && dm.UserId == userId);
                if (!isAssigned) return null;
            }
        }

        return conv;
    }

    /// <summary>Highest sequence number hidden for this user: cleared for everyone, or cleared by the user.</summary>
    private async Task<long> GetClearedUpToSequenceAsync(Conversation conv, Guid userId)
    {
        var ownClear = await _dbContext.ConversationUserClears
            .Where(c => c.ConversationId == conv.Id && c.UserId == userId)
            .Select(c => (long?)c.ClearedUpToSequence)
            .FirstOrDefaultAsync() ?? 0;
        return Math.Max(conv.ClearedUpToSequence, ownClear);
    }

    private async Task<HashSet<Guid>> GetPinnedMessageIdsAsync(Guid conversationId, Guid userId)
    {
        var ids = await _dbContext.MessagePins
            .Where(p => p.UserId == userId && p.ConversationId == conversationId)
            .Select(p => p.MessageId)
            .ToListAsync();
        return ids.ToHashSet();
    }

    private static ChatMessageDto ToMessageDto(Message m, HashSet<Guid> pinnedIds) => new(
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
        m.IsForwarded,
        pinnedIds.Contains(m.Id),
        m.ExpiresAt);

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

        // Return all registered customers in the client organization
        var availableUsers = users;

        var result = new List<AvailableDepartmentUserDto>();
        foreach (var u in availableUsers)
        {
            var isOnline = await _presenceService.IsUserOnlineAsync(u.Id);
            var hasExistingChat = existingConvUserIds.Contains(u.Id);
            result.Add(new AvailableDepartmentUserDto(
                u.Id,
                u.FullName,
                u.Email ?? u.PhoneNumber ?? "",
                u.Role,
                isOnline,
                hasExistingChat,
                u.IsBlocked
            ));
        }

        // Sort customers with active conversations first, then online status, then alphabetical
        return Ok(result.OrderByDescending(u => u.HasExistingChat).ThenByDescending(u => u.IsOnline).ThenBy(u => u.FullName).ToList());
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
