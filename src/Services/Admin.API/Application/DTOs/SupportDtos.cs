using System.ComponentModel.DataAnnotations;

namespace Admin.API.Application.DTOs;

public enum AccountStatusValue {
    Active,
    TradeRestricted,
    Banned
}

public sealed record ModerationRequest(
    [param: Required] AccountStatusValue? Status,
    [param: Required, MaxLength(500)] string Reason
);

public sealed record InternalModerationRequest(
    Guid OperationId,
    Guid ActorId,
    AccountStatusValue Status,
    string Reason
);

public sealed record AccessStatusDto(
    Guid UserId,
    string Role,
    AccountStatusValue Status,
    bool CanTrade,
    long Version
);

public sealed record ModerationResultDto(
    Guid ActionId,
    Guid TargetUserId,
    AccountStatusValue Status,
    long Version,
    DateTimeOffset UpdatedAt
);

public sealed record TradeDto(
    Guid TradeId,
    Guid ListingId,
    Guid BuyerId,
    Guid SellerId,
    Guid ItemId,
    Guid TemplateId,
    int Quantity,
    long UnitPrice,
    long GrossGem,
    long FeeGem,
    long SellerNetGem,
    string Status,
    string? FailureCode,
    bool RequiresReview,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt
);

public sealed record TradePageDto(
    IReadOnlyList<TradeDto> Items,
    int Page,
    int PageSize,
    long TotalCount
);

public sealed record SagaStepDto(
    string Step,
    string Outcome,
    DateTimeOffset At,
    string? ErrorCode
);

public sealed record MarketTradeTraceDto(
    TradeDto Trade,
    Guid CorrelationId,
    IReadOnlyList<SagaStepDto> Steps
);

public sealed record FundHoldDto(
    Guid TradeId,
    Guid BuyerId,
    long Amount,
    string Status,
    DateTimeOffset UpdatedAt
);

public sealed record WalletTransactionDto(
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
    IReadOnlyList<WalletTransactionDto> Entries
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

public sealed record SupportTradeTraceDto(
    MarketTradeTraceDto Marketplace,
    WalletTradeTraceDto? Wallet,
    ItemTransferTraceDto? Inventory,
    bool Partial,
    IReadOnlyList<string> UnavailableSources
);
