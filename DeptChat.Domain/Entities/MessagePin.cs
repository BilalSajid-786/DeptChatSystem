namespace DeptChat.Domain.Entities;

/// <summary>
/// A message pinned by one person for themselves (personal pin: nobody else sees it).
/// </summary>
public class MessagePin
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid MessageId { get; set; }
    public DateTime PinnedAt { get; set; } = DateTime.UtcNow;
}
