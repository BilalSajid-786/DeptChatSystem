using System.Collections.Concurrent;
using DeptChat.Application.Common.Interfaces;

namespace DeptChat.Infrastructure.Services;

public class InMemoryPresenceService : IPresenceService
{
    private readonly ConcurrentDictionary<Guid, string> _presence = new();
    private readonly ConcurrentDictionary<Guid, bool> _onlineStatus = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _lastSeen = new();

    public Task SetPresenceAsync(Guid userId, string screenIdentifier, TimeSpan? expiry = null)
    {
        _presence[userId] = screenIdentifier;
        _onlineStatus[userId] = true;
        return Task.CompletedTask;
    }

    public Task ClearPresenceAsync(Guid userId)
    {
        _presence.TryRemove(userId, out _);
        _onlineStatus[userId] = false;
        _lastSeen[userId] = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task<string?> GetPresenceAsync(Guid userId)
    {
        _presence.TryGetValue(userId, out var val);
        return Task.FromResult<string?>(val);
    }

    public Task SetUserOnlineAsync(Guid userId, bool isOnline)
    {
        _onlineStatus[userId] = isOnline;
        if (!isOnline)
        {
            _lastSeen[userId] = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task<bool> IsUserOnlineAsync(Guid userId)
    {
        _onlineStatus.TryGetValue(userId, out var online);
        return Task.FromResult(online);
    }

    public Task<DateTime?> GetLastSeenAsync(Guid userId)
    {
        if (_lastSeen.TryGetValue(userId, out var lastSeen))
        {
            return Task.FromResult<DateTime?>(lastSeen);
        }
        return Task.FromResult<DateTime?>(null);
    }

    public Task<bool> IsAnyStaffActiveInRoomAsync(Guid conversationId, IEnumerable<Guid> staffUserIds)
    {
        var targetRoomString = $"room:{conversationId}";

        foreach (var staffId in staffUserIds)
        {
            if (_presence.TryGetValue(staffId, out var presence) &&
                string.Equals(presence, targetRoomString, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }
}
