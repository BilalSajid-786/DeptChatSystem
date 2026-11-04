namespace DeptChat.Shared.DTOs;

public record DepartmentDto(
    Guid Id,
    string Name,
    string Description,
    bool IsActive,
    int UnreadCount,
    string? LastMessageSenderName = null,
    string? LastMessagePreview = null,
    DateTime? LastMessageAt = null
);

public record CreateDepartmentDto(
    string Name,
    string Description
);

public record AssignManagerDto(
    Guid DepartmentId,
    Guid UserId
);
