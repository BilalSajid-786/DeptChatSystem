namespace DeptChat.Application.Common.Interfaces;

public interface IPresenceService
{
    Task SetPresenceAsync(Guid userId, string screenIdentifier, TimeSpan? expiry = null);
    Task ClearPresenceAsync(Guid userId);
    Task<string?> GetPresenceAsync(Guid userId);
    Task<bool> IsAnyStaffActiveInRoomAsync(Guid conversationId, IEnumerable<Guid> staffUserIds);

    Task SetUserOnlineAsync(Guid userId, bool isOnline);
    Task<bool> IsUserOnlineAsync(Guid userId);
    Task<DateTime?> GetLastSeenAsync(Guid userId);
}
