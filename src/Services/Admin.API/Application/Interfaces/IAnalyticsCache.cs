using Admin.API.Application.DTOs;

namespace Admin.API.Application.Interfaces;

public interface IAnalyticsCache {
    Task<MarketStatisticDto?> GetAsync(DateOnly day, Guid templateId, CancellationToken cancellationToken);
    Task SetAsync(MarketStatisticDto statistic, CancellationToken cancellationToken);
}
