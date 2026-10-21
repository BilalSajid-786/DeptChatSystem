using DeptChat.Shared.Enums;

namespace DeptChat.Application.Common.Interfaces;

public interface IJwtTokenService
{
    string GenerateToken(Guid userId, string fullName, string email, UserRole role, IEnumerable<Guid>? departmentIds = null);
}
