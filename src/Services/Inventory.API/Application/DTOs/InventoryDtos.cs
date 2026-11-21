using Inventory.API.Domain.Enums;

namespace Inventory.API.Application.DTOs;

public sealed record InventoryItemDto(
    Guid ItemId,
    Guid OwnerId,
    Guid TemplateId,
    string Name,
    string Category,
    string Rarity,
    bool Tradable,
    int Quantity,
    InventoryItemStatus Status,
    Guid? ListingId,
    Guid? TradeId
);

public sealed record InventoryViewDto(
    Guid UserId,
    int Capacity,
    int UsedSlots,
    IReadOnlyList<InventoryItemDto> Items
);

public sealed record GrantItemRequest(
    Guid UserId,
    Guid TemplateId,
    int Quantity
);

public sealed record LockItemRequest(
    Guid ListingId,
    Guid ItemId,
    Guid SellerId,
    int Quantity,
    Guid TemplateId
);

public sealed record LockItemResultDto(
    Guid ListingId,
    InventoryItemDto Item,
    DateTimeOffset LockedAt
);

public sealed record UnlockItemRequest(
    Guid ItemId,
    Guid SellerId
);

public enum ItemLockLookupStatus
{
    Locked,
    Released,
    NotFound
}

public sealed record ItemLockStatusDto(
    Guid ListingId,
    Guid? ItemId,
    ItemLockLookupStatus Status
);