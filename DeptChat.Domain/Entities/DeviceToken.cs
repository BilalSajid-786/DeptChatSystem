namespace DeptChat.Domain.Entities;

public class DeviceToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string Token { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty; // "Android", "iOS", "Windows"
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
