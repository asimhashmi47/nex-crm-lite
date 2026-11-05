using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NexCrm.Application.Common;
using NexCrm.Application.Features.Auth;
using NexCrm.Domain.Entities;

namespace NexCrm.Infrastructure.Services;

public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
}

public sealed class JwtTokenService(IOptions<JwtOptions> options) : ITokenService
{
    /// <summary>The user's session version when the token was issued; a sign-out bumps it.</summary>
    public const string SessionClaim = "sv";

    public (string Token, DateTime ExpiresAt) CreateAccessToken(User user)
    {
        var o = options.Value;
        var expires = DateTime.UtcNow.AddMinutes(o.AccessTokenMinutes);
        Claim[] claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("name", user.FullName),
            new("role", user.Role.ToString()),
            new(SessionClaim, user.SessionVersion.ToString(CultureInfo.InvariantCulture)),
        ];

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.SigningKey));
        var token = new JwtSecurityToken(o.Issuer, o.Audience, claims, DateTime.UtcNow, expires, new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
