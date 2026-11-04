using DeptChat.Application.Common.Interfaces;
using StackExchange.Redis;

namespace DeptChat.Infrastructure.Services;

public class RedisPresenceService : IPresenceService
{
    private readonly IConnectionMultiplexer _redis;
    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultOnlineExpiry = TimeSpan.FromHours(24);

    public RedisPresenceService(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task SetPresenceAsync(Guid userId, string screenIdentifier, TimeSpan? expiry = null)
    {
        if (userId == Guid.Empty) return;
        var db = _redis.GetDatabase();
        var key = $"presence:{userId}";
        await db.StringSetAsync(key, screenIdentifier, expiry ?? DefaultExpiry);
    }

    public async Task ClearPresenceAsync(Guid userId)
    {
        if (userId == Guid.Empty) return;
        var db = _redis.GetDatabase();
        var key = $"presence:{userId}";
        await db.KeyDeleteAsync(key);
    }

    public async Task<string?> GetPresenceAsync(Guid userId)
    {
        if (userId == Guid.Empty) return null;
        var db = _redis.GetDatabase();
        var key = $"presence:{userId}";
        var value = await db.StringGetAsync(key);
        return value.HasValue ? value.ToString() : null;
    }

    /// <summary>
    /// Tracks an active SignalR connection for a user.
    /// Returns true if this is the user's first active connection (offline -> online transition).
    /// </summary>
    public async Task<bool> TrackConnectionAsync(Guid userId, string connectionId)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(connectionId))
            return false;

        var db = _redis.GetDatabase();
        var setKey = $"connections:{userId}";
        var onlineKey = $"online:{userId}";

        await db.SetAddAsync(setKey, connectionId);
        await db.KeyExpireAsync(setKey, DefaultOnlineExpiry);
        await db.StringSetAsync(onlineKey, "1", DefaultOnlineExpiry);

        var count = await db.SetLengthAsync(setKey);
        return count == 1;
    }

    /// <summary>
    /// Removes a closed SignalR connection for a user.
    /// Returns true ONLY if all connections for this user are now closed (online -> offline transition).
    /// </summary>
    public async Task<bool> RemoveConnectionAsync(Guid userId, string connectionId)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(connectionId))
            return false;

        var db = _redis.GetDatabase();
        var setKey = $"connections:{userId}";
        var onlineKey = $"online:{userId}";
        var lastSeenKey = $"lastseen:{userId}";

        var wasRemoved = await db.SetRemoveAsync(setKey, connectionId);
        var remainingCount = await db.SetLengthAsync(setKey);

        if (remainingCount == 0)
        {
            // All active connections are closed: mark user offline
            var hadOnlineKey = await db.KeyExistsAsync(onlineKey);
            await db.KeyDeleteAsync(onlineKey);
            await db.KeyDeleteAsync(setKey);
            await db.StringSetAsync(lastSeenKey, DateTime.UtcNow.ToString("o"));
            return wasRemoved || hadOnlineKey;
        }

        // Other connection(s) still active: keep user online
        return false;
    }

    public async Task SetUserOnlineAsync(Guid userId, bool isOnline)
    {
        if (userId == Guid.Empty) return;

        var db = _redis.GetDatabase();
        var setKey = $"connections:{userId}";
        var onlineKey = $"online:{userId}";
        var lastSeenKey = $"lastseen:{userId}";

        if (isOnline)
        {
            await db.StringSetAsync(onlineKey, "1", DefaultOnlineExpiry);
        }
        else
        {
            await db.KeyDeleteAsync(onlineKey);
            await db.KeyDeleteAsync(setKey);
            await db.StringSetAsync(lastSeenKey, DateTime.UtcNow.ToString("o"));
        }
    }

    public async Task<bool> IsUserOnlineAsync(Guid userId)
    {
        if (userId == Guid.Empty) return false;

        var db = _redis.GetDatabase();
        if (await db.KeyExistsAsync($"online:{userId}"))
            return true;

        var count = await db.SetLengthAsync($"connections:{userId}");
        return count > 0;
    }

    public async Task<DateTime?> GetLastSeenAsync(Guid userId)
    {
        if (userId == Guid.Empty) return null;

        var db = _redis.GetDatabase();
        var val = await db.StringGetAsync($"lastseen:{userId}");
        if (val.HasValue && DateTime.TryParse(val.ToString(), out var dt))
        {
            return dt;
        }
        return null;
    }

    public async Task<bool> IsAnyStaffActiveInRoomAsync(Guid conversationId, IEnumerable<Guid> staffUserIds)
    {
        var db = _redis.GetDatabase();
        var targetRoomString = $"room:{conversationId}";

        foreach (var staffId in staffUserIds)
        {
            var presence = await db.StringGetAsync($"presence:{staffId}");
            if (presence.HasValue && string.Equals(presence.ToString(), targetRoomString, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
