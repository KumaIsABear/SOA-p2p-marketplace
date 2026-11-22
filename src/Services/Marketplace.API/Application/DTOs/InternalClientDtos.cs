namespace Marketplace.API.Application.DTOs;

public sealed record AuthAccessStatusDto(
    Guid UserId,
    string Role,
    string Status,
    bool CanTrade,
    long Version
);

public sealed record InventoryItemDto(
    Guid ItemId,
    Guid OwnerId,
    Guid TemplateId,
    string Name,
    string Category,
    string Rarity,
    bool Tradable,
    int Quantity,
    string Status,
    Guid? ListingId,
    Guid? TradeId
);

public sealed record LockItemRequestDto(
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

public sealed record UnlockItemRequestDto(
    Guid ItemId,
    Guid SellerId
);

public sealed record ItemLockStatusDto(
    Guid ListingId,
    Guid? ItemId,
    string Status
);

public enum LockCallOutcomeType
{
    Succeeded,
    BusinessRejected,
    UnknownFailure
}

public sealed record LockCallResult(
    LockCallOutcomeType Outcome,
    LockItemResultDto? Data = null,
    int? StatusCode = null,
    string? ErrorCode = null,
    string? ErrorDetail = null
);
