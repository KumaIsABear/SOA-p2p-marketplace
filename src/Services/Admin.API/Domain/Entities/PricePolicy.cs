namespace Admin.API.Domain.Entities;

public sealed class PricePolicy {
    public Guid TemplateId { get; set; }
    public long BasePrice { get; set; }
    public int BandBasisPoints { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<PricePolicyAudit> Audits { get; set; } = new List<PricePolicyAudit>();
}
