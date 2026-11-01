using DeptChat.Shared.Enums;

namespace DeptChat.Shared.DTOs;

public record RegisterRequestDto(
    string FullName,
    string? Email,
    string Password,
    string CompanyCode,
    string? PhoneNumber = null,
    string? Address = null
);

public record LoginRequestDto(
    string Email, // Can be Email address or Phone number
    string Password
);

public record AuthResponseDto(
    Guid UserId,
    string FullName,
    string? Email,
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
    string? Email,
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

public record CreateUserRequestDto(
    string FullName,
    string? Email,
    string? PhoneNumber,
    string Password,
    UserRole Role,
    string? Address = null
);

public record UserSummaryDto(
    Guid Id,
    string FullName,
    string? Email,
    string? PhoneNumber,
    UserRole Role,
    string? Address,
    DateTime CreatedAt,
    bool IsBlocked = false
);

public record ClientSummaryDto(
    Guid Id,
    string Name,
    string Code,
    string? Description,
    bool IsActive,
    bool IsBlocked,
    DateTime CreatedAt
);
