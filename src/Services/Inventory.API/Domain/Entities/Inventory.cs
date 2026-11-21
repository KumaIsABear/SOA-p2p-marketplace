namespace Inventory.API.Domain.Entities;

public sealed class Inventory
{
    public Guid UserId { get; set; }
    public int Capacity { get; set; } = 20;
    public int ReservedSlots { get; set; }
    public long Version { get; set; } = 1;

    public ICollection<InventoryItem> Items { get; set; } = new List<InventoryItem>();
}