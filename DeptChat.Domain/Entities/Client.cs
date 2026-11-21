namespace DeptChat.Domain.Entities;

public class Client
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty; // Unique company code (e.g. "MAIN", "ACME")
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsBlocked { get; set; } = false;
    public DateTime? BlockedAt { get; set; }

    // Disappearing messages for the whole company (admins set it): new messages disappear this many
    // seconds after they are sent. 0 = off. Default 7 days. Messages sent earlier keep their own time.
    public int DisappearingMessagesSeconds { get; set; } = 604800;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Department> Departments { get; set; } = new List<Department>();
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
}
