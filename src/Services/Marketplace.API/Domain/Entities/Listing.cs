using Marketplace.Contracts.V1;

namespace Marketplace.API.Domain.Entities;

public sealed class Listing
{
    public Guid ListingId { get; set; }
    public Guid SellerId { get; set; }
    public Guid ItemId { get; set; }
    public Guid TemplateId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Rarity { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public long UnitPrice { get; set; }
    public long GrossGem { get; set; }
    public long FeeGem { get; set; }
    public long SellerNetGem { get; set; }
    public int FeeBasisPoints { get; set; }
    public long PolicyVersion { get; set; }
    public ListingStatus Status { get; set; }
    public long Version { get; set; }
    public DateTimeOffset? InventoryUnlockedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ListingSnapshot ToSnapshot() => new(
        ListingId,
        SellerId,
        ItemId,
        TemplateId,
        ItemName,
        Category,
        Rarity,
        Quantity,
        UnitPrice,
        GrossGem,
        Status,
        Version,
        CreatedAt,
        UpdatedAt
    );
}
