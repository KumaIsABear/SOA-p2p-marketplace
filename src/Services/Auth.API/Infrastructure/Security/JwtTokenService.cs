using System.Security.Cryptography;
using System.Text;
using Auth.API.Application.Interfaces;
using Auth.API.Application.Options;
using Auth.API.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Auth.API.Infrastructure.Security;

public sealed class JwtTokenService : ITokenService {
    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly JsonWebTokenHandler _tokenHandler = new();

    public JwtTokenService(IOptions<JwtOptions> options) {
        _options = options.Value;
        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
    }

    public GeneratedAccessToken GenerateAccessToken(User user, DateTimeOffset now) {
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Claims = new Dictionary<string, object> {
                [JwtRegisteredClaimNames.Sub] = user.UserId.ToString("D"),
                ["role"] = user.Role.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("D")
            },
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256)
        };

        var token = _tokenHandler.CreateToken(descriptor);
        return new GeneratedAccessToken(token, _options.AccessTokenMinutes * 60);
    }

    public GeneratedRefreshToken GenerateRefreshToken(DateTimeOffset now) {
        var rawBytes = RandomNumberGenerator.GetBytes(64);
        var rawToken = Base64UrlEncoder.Encode(rawBytes);
        var tokenHash = HashRefreshToken(rawToken);
        var expiresAt = now.AddDays(_options.RefreshTokenDays);

        return new GeneratedRefreshToken(rawToken, tokenHash, expiresAt);
    }

    public string HashRefreshToken(string rawToken) {
        var bytes = Encoding.UTF8.GetBytes(rawToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
