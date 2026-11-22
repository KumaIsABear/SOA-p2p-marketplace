namespace Admin.API.Domain.Entities;

public sealed class ModerationAction {
    public Guid ActionId { get; set; }
    public Guid OperationId { get; set; }
    public Guid ActorId { get; set; }
    public Guid TargetUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
