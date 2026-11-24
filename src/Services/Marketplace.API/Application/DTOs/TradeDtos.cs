using Marketplace.API.Domain.Enums;

namespace Marketplace.API.Application.DTOs;

public sealed record CatalogSearchQuery(
    string? Query,
    string? Category,
    string? Rarity,
    long? MinPrice,
    long? MaxPrice,
    string Sort,
    int Page,
    int PageSize
);

public sealed record TradeHistoryQuery(
    string Side,
    TradeStatus Status,
    int Page,
    int PageSize,
    DateTimeOffset? From,
    DateTimeOffset? To
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
    TradeStatus Status,
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

public sealed record PurchaseAcceptedDto(
    Guid TradeId,
    Guid ListingId,
    TradeStatus Status,
    long GrossGem,
    long FeeGem,
    long SellerNetGem,
    string StatusUrl
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

public sealed record MarketListingStatsDto(
    long ActiveListingCount,
    DateTimeOffset AsOf
);
