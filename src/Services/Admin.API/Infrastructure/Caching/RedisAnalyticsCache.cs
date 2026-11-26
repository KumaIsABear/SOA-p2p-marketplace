using System.Text.Json;
using System.Text.Json.Serialization;
using Admin.API.Application.DTOs;
using Admin.API.Application.Interfaces;
using Marketplace.Contracts.V1;
using StackExchange.Redis;

namespace Admin.API.Infrastructure.Caching;

public sealed class RedisAnalyticsCache : IAnalyticsCache {
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IConnectionMultiplexer _redis;

    public RedisAnalyticsCache(IConnectionMultiplexer redis) {
        _redis = redis;
    }

    public async Task<MarketStatisticDto?> GetAsync(DateOnly day, Guid templateId, CancellationToken cancellationToken) {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(CacheKeys.Analytics(day, templateId));
        if (value.IsNullOrEmpty) {
            return null;
        }

        return JsonSerializer.Deserialize<MarketStatisticDto>(value.ToString(), JsonOptions);
    }

    public async Task SetAsync(MarketStatisticDto statistic, CancellationToken cancellationToken) {
        var db = _redis.GetDatabase();
        var key = CacheKeys.Analytics(statistic.Day, statistic.TemplateId);
        var payload = JsonSerializer.Serialize(statistic, JsonOptions);
        await db.StringSetAsync(key, payload, DefaultTtl);
    }
}
