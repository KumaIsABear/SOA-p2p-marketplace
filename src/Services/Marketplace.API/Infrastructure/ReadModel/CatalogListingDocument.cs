using Marketplace.API.Application.DTOs;
using Marketplace.Contracts.V1;
using MongoDB.Bson.Serialization.Attributes;

namespace Marketplace.API.Infrastructure.ReadModel;

public sealed class CatalogListingDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("version")]
    public long Version { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = string.Empty;

    [BsonElement("sellerId")]
    public string SellerId { get; set; } = string.Empty;

    [BsonElement("itemId")]
    public string ItemId { get; set; } = string.Empty;

    [BsonElement("templateId")]
    public string TemplateId { get; set; } = string.Empty;

    [BsonElement("itemName")]
    public string ItemName { get; set; } = string.Empty;

    [BsonElement("category")]
    public string Category { get; set; } = string.Empty;

    [BsonElement("rarity")]
    public string Rarity { get; set; } = string.Empty;

    [BsonElement("quantity")]
    public int Quantity { get; set; }

    [BsonElement("unitPrice")]
    public long UnitPrice { get; set; }

    [BsonElement("grossGem")]
    public long GrossGem { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    public ListingDto ToDto() => new(
        ListingId: Guid.Parse(Id),
        SellerId: Guid.Parse(SellerId),
        ItemId: Guid.Parse(ItemId),
        TemplateId: Guid.Parse(TemplateId),
        ItemName: ItemName,
        Category: Category,
        Rarity: Rarity,
        Quantity: Quantity,
        UnitPrice: UnitPrice,
        GrossGem: GrossGem,
        Status: Enum.Parse<ListingStatus>(Status),
        Version: Version,
        CreatedAt: new DateTimeOffset(DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc)),
        UpdatedAt: new DateTimeOffset(DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc))
    );
}
