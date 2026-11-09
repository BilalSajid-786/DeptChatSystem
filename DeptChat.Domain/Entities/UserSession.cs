namespace DeptChat.Domain.Entities;

public class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string DeviceName { get; set; } = string.Empty;
    public string DevicePlatform { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
}
