using Inventory.API.Domain.Enums;

namespace Inventory.API.Domain.Entities;

public sealed class ItemTransfer
{
    public Guid TradeId { get; set; }
    public Guid ItemId { get; set; }
    public Guid SellerId { get; set; }
    public Guid BuyerId { get; set; }
    public ItemTransferStatus Status { get; set; }
    public int Quantity { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}