namespace Admin.API.Application.DTOs;

public sealed record WalletSupplyDto(
    long TotalGem,
    long HeldGem,
    long AvailableGem,
    DateTimeOffset AsOf
);

public sealed record MarketListingStatsDto(
    long ActiveListingCount,
    DateTimeOffset AsOf
);

public sealed record MarketStatisticDto(
    DateOnly Day,
    Guid TemplateId,
    long TradeCount,
    long Volume,
    long TotalGrossGem,
    long TotalFeeGem,
    long? MinUnitPrice,
    long? MaxUnitPrice,
    long ReferencePrice,
    DateTimeOffset AsOf
);

public sealed record MarketStatisticPageDto(
    IReadOnlyList<MarketStatisticDto> Items,
    int Page,
    int PageSize,
    long TotalCount
);

public sealed record MarketOverviewDto(
    DateOnly Day,
    long TradeCount,
    long Volume,
    long TotalGrossGem,
    long TotalFeeGem,
    WalletSupplyDto? GemSupply,
    long? ActiveListingCount,
    bool Partial,
    IReadOnlyList<string> UnavailableSources,
    DateTimeOffset AsOf
);
