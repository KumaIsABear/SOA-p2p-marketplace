using Auth.API.Domain.Enums;

namespace Auth.API.Domain.Entities;

public sealed class User {
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public UserRole Role { get; set; }
    public AccountStatus Status { get; set; }
    public long Version { get; set; } = 1;

    public ICollection<ExternalIdentity> ExternalIdentities { get; set; } = new List<ExternalIdentity>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
