using System.Collections.Concurrent;
using DeptChat.Application.Common.Interfaces;

namespace DeptChat.Infrastructure.Services;

public class InMemoryPresenceService : IPresenceService
{
    private readonly ConcurrentDictionary<Guid, string> _presence = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> _userConnections = new();
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
        return Task.CompletedTask;
    }

    public Task<string?> GetPresenceAsync(Guid userId)
    {
        _presence.TryGetValue(userId, out var val);
        return Task.FromResult<string?>(val);
    }

    public Task<bool> TrackConnectionAsync(Guid userId, string connectionId)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(connectionId))
            return Task.FromResult(false);

        var conns = _userConnections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>());
        conns[connectionId] = 0;
        _onlineStatus[userId] = true;

        return Task.FromResult(conns.Count == 1);
    }

    public Task<bool> RemoveConnectionAsync(Guid userId, string connectionId)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(connectionId))
            return Task.FromResult(false);

        if (_userConnections.TryGetValue(userId, out var conns))
        {
            conns.TryRemove(connectionId, out _);
            if (conns.IsEmpty)
            {
                _userConnections.TryRemove(userId, out _);
                _onlineStatus[userId] = false;
                _lastSeen[userId] = DateTime.UtcNow;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        _onlineStatus[userId] = false;
        _lastSeen[userId] = DateTime.UtcNow;
        return Task.FromResult(true);
    }

    public Task SetUserOnlineAsync(Guid userId, bool isOnline)
    {
        _onlineStatus[userId] = isOnline;
        if (!isOnline)
        {
            _userConnections.TryRemove(userId, out _);
            _lastSeen[userId] = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task<bool> IsUserOnlineAsync(Guid userId)
    {
        if (_userConnections.TryGetValue(userId, out var conns) && !conns.IsEmpty)
            return Task.FromResult(true);

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
