namespace Admin.API.Domain.Entities;

public sealed class TradeFact {
    public Guid TradeId { get; set; }
    public Guid TemplateId { get; set; }
    public long GrossGem { get; set; }
    public long FeeGem { get; set; }
    public int Quantity { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
}
