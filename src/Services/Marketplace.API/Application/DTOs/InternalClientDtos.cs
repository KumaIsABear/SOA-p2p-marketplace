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

public sealed record ItemTransferTraceDto(
    Guid TradeId,
    Guid ItemId,
    Guid SellerId,
    Guid BuyerId,
    int Quantity,
    string Status,
    DateTimeOffset UpdatedAt
);

public sealed record FundHoldDto(
    Guid TradeId,
    Guid BuyerId,
    long Amount,
    string Status,
    DateTimeOffset UpdatedAt
);

public sealed record WalletTransactionTraceDto(
    Guid EntryId,
    Guid UserId,
    Guid? TradeId,
    Guid? TopUpId,
    long TotalDelta,
    long HoldDelta,
    long? FeeGem,
    string Type,
    DateTimeOffset CreatedAt
);

public sealed record WalletTradeTraceDto(
    Guid TradeId,
    FundHoldDto? Hold,
    string SettlementStatus,
    IReadOnlyList<WalletTransactionTraceDto> Entries
);
