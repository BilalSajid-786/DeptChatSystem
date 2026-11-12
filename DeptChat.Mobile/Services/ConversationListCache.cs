using System.Collections.Concurrent;
using DeptChat.Shared.DTOs;

namespace DeptChat.Mobile.Services;

/// <summary>
/// In-memory (never written to disk) cache of the last staff conversation list per department,
/// so reopening a department shows its list instantly while fresh data loads.
/// Keyed by signed-in user + department so one user can never see another user's list,
/// and cleared on logout (AuthService.Logout).
/// </summary>
public static class ConversationListCache
{
    private static readonly ConcurrentDictionary<string, List<ConversationSummaryDto>> _cache = new();

    public static string? BuildKey(Guid? userId, Guid departmentId)
    {
        if (userId == null || userId == Guid.Empty) return null;
        return $"{userId}:{departmentId}";
    }

    public static bool TryGet(string key, out List<ConversationSummaryDto> list)
    {
        if (_cache.TryGetValue(key, out var found))
        {
            list = found;
            return true;
        }

        list = new List<ConversationSummaryDto>();
        return false;
    }

    public static void Set(string key, List<ConversationSummaryDto> list)
    {
        _cache[key] = list;
    }

    public static void Clear()
    {
        _cache.Clear();
    }
}
