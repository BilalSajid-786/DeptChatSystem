using DeptChat.Shared.Enums;

namespace DeptChat.Shared.DTOs;

public record RegisterRequestDto(
    string FullName,
    string Email,
    string Password,
    string CompanyCode,
    string? PhoneNumber = null,
    string? Address = null
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
    List<Guid> DepartmentIds,
    Guid ClientId,
    string ClientName,
    string? PhoneNumber = null,
    string? Address = null
);

public record UserProfileDto(
    Guid UserId,
    string FullName,
    string Email,
    UserRole Role,
    Guid ClientId,
    string ClientName,
    string? PhoneNumber,
    string? Address
);

public record UpdateProfileRequestDto(
    string FullName,
    string? PhoneNumber,
    string? Address
);

public record RegisterDeviceTokenDto(
    string Token,
    string Platform
);
