using DeptChat.Shared.Enums;

namespace DeptChat.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public string FullName { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenAt { get; set; }
    public bool IsBlocked { get; set; } = false;
    public DateTime? BlockedAt { get; set; }
    public Guid? BlockedByUserId { get; set; }

    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public ICollection<DepartmentManager> DepartmentAssignments { get; set; } = new List<DepartmentManager>();
    public ICollection<Conversation> CreatedConversations { get; set; } = new List<Conversation>();
    public ICollection<DeviceToken> DeviceTokens { get; set; } = new List<DeviceToken>();
    public ICollection<UserSession> Sessions { get; set; } = new List<UserSession>();
}
