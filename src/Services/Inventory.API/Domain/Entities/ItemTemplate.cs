namespace Inventory.API.Domain.Entities;

public sealed class ItemTemplate
{
    public Guid TemplateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Rarity { get; set; } = string.Empty;
    public bool Tradable { get; set; }

    public ICollection<InventoryItem> Items { get; set; } = new List<InventoryItem>();
}