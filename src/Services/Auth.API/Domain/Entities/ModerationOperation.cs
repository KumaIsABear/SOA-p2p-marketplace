using Auth.API.Domain.Enums;

namespace Auth.API.Domain.Entities;

public sealed class ModerationOperation {
    public Guid OperationId { get; set; }
    public Guid TargetUserId { get; set; }
    public Guid ActorId { get; set; }
    public AccountStatus TargetStatus { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public long ResultVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User TargetUser { get; set; } = null!;
}
