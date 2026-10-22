using DeptChat.Shared.Enums;

namespace DeptChat.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.User;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenAt { get; set; }

    public ICollection<DepartmentManager> DepartmentAssignments { get; set; } = new List<DepartmentManager>();
    public ICollection<Conversation> CreatedConversations { get; set; } = new List<Conversation>();
    public ICollection<DeviceToken> DeviceTokens { get; set; } = new List<DeviceToken>();
}
