using Admin.API.Application.Common;
using Admin.API.Application.DTOs;
using Marketplace.Contracts.V1;

namespace Admin.API.Application.Interfaces;

public interface IMarketAnalyticsService {
    Task RecordTradeCompletedAsync(TradeCompletedEvent tradeEvent, CancellationToken cancellationToken);
    Task ReconcileDayAsync(DateOnly businessDay, CancellationToken cancellationToken);
    Task<AdminResult<MarketOverviewDto>> GetOverviewAsync(DateOnly? day, string correlationId, CancellationToken cancellationToken);
    Task<AdminResult<MarketStatisticPageDto>> GetHistoryAsync(
        Guid? templateId,
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken
    );
    DateOnly GetCurrentBusinessDay();
}
