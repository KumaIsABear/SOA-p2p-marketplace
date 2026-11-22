namespace Admin.API.Domain.Entities;

public sealed class PricePolicyAudit {
    public Guid AuditId { get; set; }
    public Guid TemplateId { get; set; }
    public Guid ActorId { get; set; }
    public long BasePrice { get; set; }
    public int BandBasisPoints { get; set; }
    public long Version { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public PricePolicy Policy { get; set; } = null!;
}
