using Marketplace.API.Application.DTOs;

namespace Marketplace.API.Application.Interfaces;

public interface IInternalMarketplaceService
{
    Task<MarketTradeTraceDto> GetTradeTraceAsync(Guid tradeId, CancellationToken ct);

    Task<TradePageDto> SearchTradesByUserAsync(
        Guid userId,
        string? status,
        int page,
        int pageSize,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct
    );

    Task<MarketListingStatsDto> GetStatsAsync(CancellationToken ct);
}
