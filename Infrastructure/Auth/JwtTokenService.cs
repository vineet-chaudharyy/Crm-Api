using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Crm_Api.Application.Interfaces;
using Crm_Api.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Crm_Api.Infrastructure.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "LeadCRM";
    public string Audience { get; set; } = "LeadCRM.Client";
    public int ExpiryHours { get; set; } = 8;
}

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _opt;

    public JwtTokenService(IOptions<JwtOptions> opt) => _opt = opt.Value;

    public (string token, DateTime expiresAt) CreateToken(Employee employee)
    {
        // Admin sessions never expire on their own — only an explicit logout clears them.
        var expires = employee.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
            ? DateTime.UtcNow.AddYears(50)
            : DateTime.UtcNow.AddHours(_opt.ExpiryHours);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, employee.EmployeeId),
            new Claim(JwtRegisteredClaimNames.Email, employee.Email),
            new Claim(ClaimTypes.Name, employee.FullName),
            new Claim(ClaimTypes.Role, employee.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
