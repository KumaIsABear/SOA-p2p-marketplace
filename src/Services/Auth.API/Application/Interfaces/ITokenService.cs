using Auth.API.Domain.Entities;

namespace Auth.API.Application.Interfaces;

public sealed record GeneratedAccessToken(string Token, int ExpiresInSeconds);
public sealed record GeneratedRefreshToken(string RawToken, string TokenHash, DateTimeOffset ExpiresAt);

public interface ITokenService {
    GeneratedAccessToken GenerateAccessToken(User user, DateTimeOffset now);
    GeneratedRefreshToken GenerateRefreshToken(DateTimeOffset now);
    string HashRefreshToken(string rawToken);
}
