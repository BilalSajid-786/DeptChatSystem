using DeptChat.Shared.Enums;

namespace DeptChat.Shared.DTOs;

public record RegisterRequestDto(
    string FullName,
    string Email,
    string Password
);

public record LoginRequestDto(
    string Email,
    string Password
);

public record AuthResponseDto(
    Guid UserId,
    string FullName,
    string Email,
    UserRole Role,
    string Token,
    List<Guid> DepartmentIds
);

public record RegisterDeviceTokenDto(
    string Token,
    string Platform
);
