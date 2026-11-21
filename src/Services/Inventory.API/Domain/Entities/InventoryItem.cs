using Inventory.API.Domain.Enums;

namespace Inventory.API.Domain.Entities;

public sealed class InventoryItem
{
    public Guid ItemId { get; set; }
    public Guid OwnerId { get; set; }
    public Guid TemplateId { get; set; }
    public int Quantity { get; set; }
    public InventoryItemStatus Status { get; set; } = InventoryItemStatus.Available;
    public Guid? ListingId { get; set; }
    public Guid? TradeId { get; set; }
    public long Version { get; set; } = 1;

    public Inventory Inventory { get; set; } = null!;
    public ItemTemplate Template { get; set; } = null!;
}