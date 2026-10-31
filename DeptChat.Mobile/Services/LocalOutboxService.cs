using DeptChat.Shared.Enums;
using SQLite;

namespace DeptChat.Mobile.Services;

[Table("OutboxMessages")]
public class LocalOutboxMessage
{
    [PrimaryKey, AutoIncrement]
    public int LocalId { get; set; }

    [Indexed]
    public Guid ConversationId { get; set; }

    [Unique]
    public string ClientGeneratedId { get; set; } = string.Empty;

    public Guid? ServerMessageId { get; set; }
    public Guid SenderId { get; set; }
    public UserRole SenderRole { get; set; } = UserRole.User;
    public long SequenceNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public bool IsOutgoing { get; set; }
    public MessageDeliveryStatus Status { get; set; } = MessageDeliveryStatus.Pending;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class LocalOutboxService
{
    private readonly SQLiteAsyncConnection _db;

    private readonly TaskCompletionSource<bool> _initTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public LocalOutboxService(string dbPath)
    {
        _db = new SQLiteAsyncConnection(dbPath);
    }

    public async Task InitializeAsync()
    {
        try
        {
            const int CurrentCacheVersion = 3;
            var cachedVer = Preferences.Default.Get("outbox_cache_version", 0);
            if (cachedVer < CurrentCacheVersion)
            {
                await _db.DropTableAsync<LocalOutboxMessage>();
                Preferences.Default.Set("outbox_cache_version", CurrentCacheVersion);
            }
            await _db.CreateTableAsync<LocalOutboxMessage>();
            _initTcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
            _initTcs.TrySetException(ex);
        }
    }

    private async Task EnsureInitializedAsync()
    {
        await _initTcs.Task;
    }

    public async Task<LocalOutboxMessage> EnqueueAsync(Guid conversationId, string clientGeneratedId, string content, string senderName, Guid senderId = default, UserRole senderRole = UserRole.User)
    {
        await EnsureInitializedAsync();
        var existing = await _db.Table<LocalOutboxMessage>()
            .FirstOrDefaultAsync(m => m.ClientGeneratedId == clientGeneratedId);

        if (existing != null)
        {
            existing.Content = content;
            existing.SenderName = senderName;
            existing.SenderId = senderId;
            existing.SenderRole = senderRole;
            existing.Status = MessageDeliveryStatus.Pending;
            existing.Timestamp = DateTime.UtcNow;
            await _db.UpdateAsync(existing);
            return existing;
        }

        var msg = new LocalOutboxMessage
        {
            ConversationId = conversationId,
            ClientGeneratedId = clientGeneratedId,
            Content = content,
            SenderName = senderName,
            SenderId = senderId,
            SenderRole = senderRole,
            IsOutgoing = true,
            Status = MessageDeliveryStatus.Pending,
            Timestamp = DateTime.UtcNow
        };

        await _db.InsertAsync(msg);
        return msg;
    }

    public async Task<List<LocalOutboxMessage>> GetPendingAsync(Guid conversationId)
    {
        await EnsureInitializedAsync();
        return await _db.Table<LocalOutboxMessage>()
            .Where(m => m.ConversationId == conversationId && m.Status == MessageDeliveryStatus.Pending)
            .ToListAsync();
    }

    public async Task UpdateAckAsync(string clientGenId, Guid serverMsgId, long sequenceNumber, MessageDeliveryStatus status)
    {
        await EnsureInitializedAsync();
        var existing = await _db.Table<LocalOutboxMessage>()
            .FirstOrDefaultAsync(m => m.ClientGeneratedId == clientGenId);

        if (existing != null)
        {
            existing.ServerMessageId = serverMsgId;
            existing.SequenceNumber = sequenceNumber;
            existing.Status = status;
            await _db.UpdateAsync(existing);
        }
    }

    public async Task UpdateDeliveredAsync(Guid serverMsgId)
    {
        await EnsureInitializedAsync();
        var existing = await _db.Table<LocalOutboxMessage>()
            .FirstOrDefaultAsync(m => m.ServerMessageId == serverMsgId);

        if (existing != null && existing.Status < MessageDeliveryStatus.Delivered)
        {
            existing.Status = MessageDeliveryStatus.Delivered;
            await _db.UpdateAsync(existing);
        }
    }

    public async Task UpdateReadAsync(Guid conversationId, long maxSequenceNumber)
    {
        await EnsureInitializedAsync();
        var messages = await _db.Table<LocalOutboxMessage>()
            .Where(m => m.ConversationId == conversationId && m.IsOutgoing && m.SequenceNumber > 0 && m.SequenceNumber <= maxSequenceNumber && m.Status < MessageDeliveryStatus.Read)
            .ToListAsync();

        foreach (var m in messages)
        {
            m.Status = MessageDeliveryStatus.Read;
            await _db.UpdateAsync(m);
        }
    }

    public async Task SaveServerMessageAsync(Guid conversationId, Guid serverMsgId, Guid senderId, UserRole senderRole, string clientGenId, string content, string senderName, bool isOutgoing, long sequenceNumber, MessageDeliveryStatus status, DateTime sentAt)
    {
        await EnsureInitializedAsync();
        var existing = await _db.Table<LocalOutboxMessage>()
            .FirstOrDefaultAsync(m => m.ClientGeneratedId == clientGenId || m.ServerMessageId == serverMsgId);

        if (existing != null)
        {
            existing.ServerMessageId = serverMsgId;
            existing.SenderId = senderId;
            existing.SenderRole = senderRole;
            existing.SequenceNumber = sequenceNumber;
            existing.Status = status;
            existing.Content = content;
            existing.IsOutgoing = isOutgoing;
            await _db.UpdateAsync(existing);
        }
        else
        {
            var msg = new LocalOutboxMessage
            {
                ConversationId = conversationId,
                ServerMessageId = serverMsgId,
                SenderId = senderId,
                SenderRole = senderRole,
                ClientGeneratedId = clientGenId,
                Content = content,
                SenderName = senderName,
                IsOutgoing = isOutgoing,
                SequenceNumber = sequenceNumber,
                Status = status,
                Timestamp = sentAt
            };
            await _db.InsertAsync(msg);
        }
    }

    public Task SaveServerMessageAsync(Guid conversationId, Guid serverMsgId, string clientGenId, string content, string senderName, bool isOutgoing, long sequenceNumber, MessageDeliveryStatus status, DateTime sentAt)
        => SaveServerMessageAsync(conversationId, serverMsgId, Guid.Empty, UserRole.User, clientGenId, content, senderName, isOutgoing, sequenceNumber, status, sentAt);

    public async Task ClearAllAsync()
    {
        try
        {
            await EnsureInitializedAsync();
            await _db.DeleteAllAsync<LocalOutboxMessage>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocalOutboxService] ClearAllAsync error: {ex.Message}");
        }
    }

    public async Task<List<LocalOutboxMessage>> GetConversationMessagesAsync(Guid conversationId)
    {
        await EnsureInitializedAsync();
        var msgs = await _db.Table<LocalOutboxMessage>()
            .Where(m => m.ConversationId == conversationId)
            .ToListAsync();

        return msgs
            .OrderBy(m => m.SequenceNumber > 0 ? m.SequenceNumber : long.MaxValue)
            .ThenBy(m => m.Timestamp)
            .ToList();
    }

    public async Task<List<LocalOutboxMessage>> GetRecentConversationMessagesAsync(Guid conversationId, int limit = 25)
    {
        await EnsureInitializedAsync();
        var msgs = await _db.Table<LocalOutboxMessage>()
            .Where(m => m.ConversationId == conversationId)
            .ToListAsync();

        var pending = msgs.Where(m => m.SequenceNumber == 0).ToList();
        var confirmed = msgs.Where(m => m.SequenceNumber > 0)
            .OrderByDescending(m => m.SequenceNumber)
            .Take(limit)
            .ToList();

        return confirmed.Concat(pending)
            .OrderBy(m => m.SequenceNumber > 0 ? m.SequenceNumber : long.MaxValue)
            .ThenBy(m => m.Timestamp)
            .ToList();
    }

    public async Task ClearConversationMessagesAsync(Guid conversationId)
    {
        await EnsureInitializedAsync();
        var msgs = await _db.Table<LocalOutboxMessage>()
            .Where(m => m.ConversationId == conversationId)
            .ToListAsync();

        foreach (var m in msgs)
        {
            await _db.DeleteAsync(m);
        }
    }

    public async Task ClearConfirmedMessagesAsync(Guid conversationId)
    {
        await EnsureInitializedAsync();
        var msgs = await _db.Table<LocalOutboxMessage>()
            .Where(m => m.ConversationId == conversationId && m.Status != MessageDeliveryStatus.Pending)
            .ToListAsync();

        foreach (var m in msgs)
        {
            await _db.DeleteAsync(m);
        }
    }

    public async Task MigrateConversationIdAsync(Guid oldConversationId, Guid newConversationId)
    {
        if (oldConversationId == Guid.Empty || newConversationId == Guid.Empty || oldConversationId == newConversationId)
            return;

        await EnsureInitializedAsync();
        var msgs = await _db.Table<LocalOutboxMessage>()
            .Where(m => m.ConversationId == oldConversationId)
            .ToListAsync();

        foreach (var m in msgs)
        {
            m.ConversationId = newConversationId;
            await _db.UpdateAsync(m);
        }
    }
}
