namespace DeptChat.Domain.Entities;

/// <summary>
/// A customer chat pinned by an admin or manager at the top of their own department chat list
/// (personal pin: nobody else sees it).
/// </summary>
public class ConversationPin
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid ConversationId { get; set; }
    public DateTime PinnedAt { get; set; } = DateTime.UtcNow;
}
