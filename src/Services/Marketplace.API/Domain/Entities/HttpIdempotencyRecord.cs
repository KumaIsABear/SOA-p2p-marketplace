namespace Marketplace.API.Domain.Entities;

public sealed class HttpIdempotencyRecord
{
    public Guid RecordId { get; set; }
    public Guid ActorId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string ResourceScope { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public Guid? ResourceId { get; set; }
    public int ResponseStatusCode { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorDetail { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
