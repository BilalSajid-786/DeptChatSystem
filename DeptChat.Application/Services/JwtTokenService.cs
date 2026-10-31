using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DeptChat.Application.Common.Interfaces;
using DeptChat.Shared.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace DeptChat.Application.Services;

public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenerateToken(Guid userId, string fullName, string? email, UserRole role, Guid clientId, string clientName, IEnumerable<Guid>? departmentIds = null)
    {
        var jwtSettings = _configuration.GetSection("JwtSettings");
        var secret = jwtSettings["Secret"] ?? "A_VERY_LONG_SUPER_SECRET_KEY_FOR_JWT_AUTHENTICATION_1234567890";
        var issuer = jwtSettings["Issuer"] ?? "DeptChatApi";
        var audience = jwtSettings["Audience"] ?? "DeptChatMobileApp";
        var expiryDays = int.TryParse(jwtSettings["ExpiryDays"], out var days) ? days : 30;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, fullName),
            new(ClaimTypes.Email, email ?? string.Empty),
            new(ClaimTypes.Role, role.ToString()),
            new("client_id", clientId.ToString()),
            new("client_name", clientName)
        };

        // Add department_id claims for Managers
        if (departmentIds != null)
        {
            foreach (var deptId in departmentIds)
            {
                claims.Add(new Claim("department_id", deptId.ToString()));
            }
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddDays(expiryDays),
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);

        return tokenHandler.WriteToken(token);
    }
}
