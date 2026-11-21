namespace DeptChat.Domain.Entities;

/// <summary>
/// "Clear chat" done by one person for themselves: messages up to and including
/// <see cref="ClearedUpToSequence"/> are hidden for this user only. Everyone else still sees them.
/// </summary>
public class ConversationUserClear
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public long ClearedUpToSequence { get; set; }
    public DateTime ClearedAt { get; set; } = DateTime.UtcNow;
}
