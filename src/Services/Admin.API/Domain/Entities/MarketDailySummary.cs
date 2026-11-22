namespace Admin.API.Domain.Entities;

public sealed class MarketDailySummary {
    public DateOnly Day { get; set; }
    public Guid TemplateId { get; set; }
    public long TradeCount { get; set; }
    public long TotalGrossGem { get; set; }
    public long TotalFeeGem { get; set; }
    public long Volume { get; set; }
    public long? MinUnitPrice { get; set; }
    public long? MaxUnitPrice { get; set; }
    public long ReferencePrice { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
