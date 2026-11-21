namespace Auth.API.Domain.Entities;

public sealed class ExternalIdentity {
    public Guid IdentityId { get; set; }
    public Guid UserId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProviderSubject { get; set; } = string.Empty;

    public User User { get; set; } = null!;
}
