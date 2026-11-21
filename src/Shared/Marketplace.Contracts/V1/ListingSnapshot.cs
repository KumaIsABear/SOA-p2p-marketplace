namespace Marketplace.Contracts.V1;

public sealed record ListingSnapshot(
    Guid ListingId,
    Guid SellerId,
    Guid ItemId,
    Guid TemplateId,
    string ItemName,
    string Category,
    string Rarity,
    int Quantity,
    long UnitPrice,
    long GrossGem,
    ListingStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
