namespace DeptChat.Domain.Entities;

public class Client
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty; // Unique company code (e.g. "MAIN", "ACME")
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Department> Departments { get; set; } = new List<Department>();
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
}
