using System.Collections.Concurrent;
using DeptChat.Application.Common.Interfaces;

namespace DeptChat.Infrastructure.Services;

public class InMemoryPresenceService : IPresenceService
{
    private readonly ConcurrentDictionary<Guid, string> _presence = new();

    public Task SetPresenceAsync(Guid userId, string screenIdentifier, TimeSpan? expiry = null)
    {
        _presence[userId] = screenIdentifier;
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
