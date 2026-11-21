namespace Inventory.API.Infrastructure.Options;

public sealed class InventoryOptions
{
    public const string SectionName = "Inventory";

    public int DefaultCapacity { get; set; } = 20;
}