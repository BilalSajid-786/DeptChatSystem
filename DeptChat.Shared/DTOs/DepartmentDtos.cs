namespace DeptChat.Shared.DTOs;

public record DepartmentDto(
    Guid Id,
    string Name,
    string Description,
    bool IsActive,
    int UnreadCount
);

public record CreateDepartmentDto(
    string Name,
    string Description
);

public record AssignManagerDto(
    Guid DepartmentId,
    Guid UserId
);
