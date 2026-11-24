using MassTransit;

namespace Marketplace.API.Domain.Entities;

public sealed class TradeSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public Guid TradeId { get; set; }
    public Guid ListingId { get; set; }
    public Guid BuyerId { get; set; }
    public Guid SellerId { get; set; }
    public Guid ItemId { get; set; }
    public Guid TemplateId { get; set; }
    public int Quantity { get; set; }
    public long UnitPrice { get; set; }
    public long GrossGem { get; set; }
    public long FeeGem { get; set; }
    public long SellerNetGem { get; set; }
    public string CurrentState { get; set; } = string.Empty;
    public Guid CurrentOperationId { get; set; }
    public int RetryCount { get; set; }
    public bool RequiresReview { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
