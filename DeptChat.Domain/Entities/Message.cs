using DeptChat.Shared.Enums;

namespace DeptChat.Domain.Entities;

public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;

    public Guid SenderId { get; set; }
    public User? Sender { get; set; }
    public UserRole SenderRole { get; set; }

    public string ClientGeneratedId { get; set; } = string.Empty;
    public long SequenceNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public MessageDeliveryStatus Status { get; set; } = MessageDeliveryStatus.Sent;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
