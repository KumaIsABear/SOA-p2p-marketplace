using Marketplace.API.Domain.Enums;

namespace Marketplace.API.Domain.Entities;

public sealed class TradeHistory
{
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
    public TradeStatus Status { get; set; }
    public string? FailureCode { get; set; }
    public bool RequiresReview { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
