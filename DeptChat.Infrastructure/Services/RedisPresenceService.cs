using DeptChat.Application.Common.Interfaces;
using StackExchange.Redis;

namespace DeptChat.Infrastructure.Services;

public class RedisPresenceService : IPresenceService
{
    private readonly IConnectionMultiplexer _redis;
    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(10);

    public RedisPresenceService(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task SetPresenceAsync(Guid userId, string screenIdentifier, TimeSpan? expiry = null)
    {
        var db = _redis.GetDatabase();
        var key = $"presence:{userId}";
        await db.StringSetAsync(key, screenIdentifier, expiry ?? DefaultExpiry);
    }

    public async Task ClearPresenceAsync(Guid userId)
    {
        var db = _redis.GetDatabase();
        var key = $"presence:{userId}";
        await db.KeyDeleteAsync(key);
    }

    public async Task<string?> GetPresenceAsync(Guid userId)
    {
        var db = _redis.GetDatabase();
        var key = $"presence:{userId}";
        var value = await db.StringGetAsync(key);
        return value.HasValue ? value.ToString() : null;
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
