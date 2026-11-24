namespace Marketplace.API.Domain.Entities;

public sealed class SagaStep
{
    public Guid StepId { get; set; }
    public Guid TradeId { get; set; }
    public string Step { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public DateTimeOffset At { get; set; }
}
