using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SecureChat.Shared.Contracts.Auth;

namespace SecureChat.Server.Auth;

public sealed class JwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public LoginResult CreateToken(string externalId, CurrentUserResponse user)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.ExpirationMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, externalId),
            new(ClaimTypes.NameIdentifier, externalId),
            new("external_id", externalId),
            new("user_id", user.UserId),
            new(JwtRegisteredClaimNames.UniqueName, user.DisplayName),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("provider", user.AuthProvider)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        return new LoginResult
        {
            AccessToken = accessToken,
            ExpiresAt = expiresAt,
            User = user
        };
    }
}
