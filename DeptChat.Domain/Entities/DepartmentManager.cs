namespace DeptChat.Domain.Entities;

public class DepartmentManager
{
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
