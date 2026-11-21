using Inventory.API.Domain.Enums;

namespace Inventory.API.Domain.Entities;

public sealed class ItemLock
{
    public Guid ListingId { get; set; }
    public Guid ItemId { get; set; }
    public Guid SellerId { get; set; }
    public Guid TemplateId { get; set; }
    public int Quantity { get; set; }
    public ItemLockStatusType Status { get; set; }
    public DateTimeOffset LockedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public InventoryItem Item { get; set; } = null!;
}