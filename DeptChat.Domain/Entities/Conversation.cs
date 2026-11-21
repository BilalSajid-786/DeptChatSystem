using DeptChat.Shared.Enums;

namespace DeptChat.Domain.Entities;

public class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public Guid CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;

    public ConversationStatus Status { get; set; } = ConversationStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string? LastMessagePreview { get; set; }
    public DateTime? LastMessageAt { get; set; }

    // "Clear chat for everyone" (Admin/Manager): messages up to and including this sequence number
    // are hidden for everyone. They stay in the database so sequence numbers keep increasing.
    public long ClearedUpToSequence { get; set; }
    public DateTime? ClearedAt { get; set; }
    public Guid? ClearedByUserId { get; set; }

    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
