using DeptChat.Shared.Enums;

namespace DeptChat.Application.Common.Interfaces;

public interface IJwtTokenService
{
    string GenerateToken(Guid userId, string fullName, string? email, UserRole role, Guid clientId, string clientName, IEnumerable<Guid>? departmentIds = null);
}
