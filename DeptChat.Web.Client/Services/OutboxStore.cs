using System.Text.Json;
using DeptChat.Shared.DTOs;

namespace DeptChat.Web.Client.Services;

/// <summary>
/// Messages sent but not yet acknowledged by the server (MessageAck). Kept in localStorage so a
/// reload or a dropped connection doesn't lose them; they are re-sent with the SAME
/// ClientGeneratedId, so the server's idempotency check prevents duplicates.
/// Voice notes are not persisted (too large for localStorage); they stay in memory only.
/// </summary>
public sealed class OutboxStore
{
    private const int MaxPersistedContentLength = 20_000;
    private readonly BrowserInterop _browser;
    private readonly AuthSession _auth;
    private readonly Dictionary<string, PendingMessage> _pending = new();
    private Guid _loadedFor;

    public OutboxStore(BrowserInterop browser, AuthSession auth)
    {
        _browser = browser;
        _auth = auth;
    }

    private string Key => $"deptchat_outbox_{_auth.UserId:N}";

    public async Task EnsureLoadedAsync()
    {
        if (_loadedFor == _auth.UserId) return;
        _pending.Clear();
        _loadedFor = _auth.UserId;
        if (_auth.UserId == Guid.Empty) return;
        try
        {
            var json = await _browser.LoadLocalAsync(Key);
            if (string.IsNullOrWhiteSpace(json)) return;
            var items = JsonSerializer.Deserialize<List<PendingMessage>>(json, JsonOptions) ?? new();
            foreach (var item in items) _pending[item.Message.ClientGeneratedId] = item;
        }
        catch
        {
            // Corrupt or blocked storage: start empty.
        }
    }

    public IReadOnlyList<PendingMessage> All => _pending.Values.OrderBy(p => p.CreatedAt).ToList();

    public IReadOnlyList<PendingMessage> ForConversation(Guid conversationId)
        => _pending.Values.Where(p => p.Message.ConversationId == conversationId).OrderBy(p => p.CreatedAt).ToList();

    public async Task AddAsync(SendMessageDto message)
    {
        await EnsureLoadedAsync();
        _pending[message.ClientGeneratedId] = new PendingMessage(message, DateTime.UtcNow);
        await PersistAsync();
    }

    public async Task RemoveAsync(string clientGeneratedId)
    {
        if (_pending.Remove(clientGeneratedId))
        {
            await PersistAsync();
        }
    }

    public async Task ClearAsync()
    {
        _pending.Clear();
        await PersistAsync();
    }

    private async Task PersistAsync()
    {
        if (_auth.UserId == Guid.Empty) return;
        try
        {
            var small = _pending.Values.Where(p => p.Message.Content.Length <= MaxPersistedContentLength).ToList();
            await _browser.SaveLocalAsync(Key, JsonSerializer.Serialize(small, JsonOptions));
        }
        catch
        {
            // Storage full or blocked: pending messages still live in memory for this tab.
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

public sealed record PendingMessage(SendMessageDto Message, DateTime CreatedAt);
