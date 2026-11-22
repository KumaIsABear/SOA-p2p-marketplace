using Marketplace.API.Application.DTOs;
using Marketplace.Contracts.V1;

namespace Marketplace.API.Application.Interfaces;

public interface ICatalogProjectionStore
{
    Task EnsureIndexesAsync(CancellationToken ct);
    Task<bool> UpsertAsync(ListingSnapshot snapshot, CancellationToken ct);
    Task<ListingPageDto> SearchActiveAsync(CatalogSearchQuery query, CancellationToken ct);
}

public interface ICatalogQueryService
{
    Task<ListingPageDto> SearchListingsAsync(
        Guid requesterId,
        string? q,
        string? category,
        string? rarity,
        long? minPrice,
        long? maxPrice,
        string? sort,
        int page,
        int pageSize,
        CancellationToken ct
    );
}

public interface ITradeQueryService
{
    Task<TradePageDto> GetHistoryAsync(
        Guid userId,
        string? side,
        string? status,
        int page,
        int pageSize,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct
    );

    Task<TradeDto> GetTradeAsync(Guid requesterId, Guid tradeId, CancellationToken ct);
}
