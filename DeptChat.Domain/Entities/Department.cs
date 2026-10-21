namespace DeptChat.Domain.Entities;

public class Department
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<DepartmentManager> Managers { get; set; } = new List<DepartmentManager>();
    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
}
