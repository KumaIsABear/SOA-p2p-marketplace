namespace Inventory.API.Domain.Entities;

public sealed class HttpIdempotencyRecord
{
    public Guid Id { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string ResourceScope { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string ResponseBody { get; set; } = string.Empty;
    public Guid? ResourceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}