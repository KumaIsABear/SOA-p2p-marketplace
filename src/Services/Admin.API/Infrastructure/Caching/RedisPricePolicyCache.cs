using System.Text.Json;
using System.Text.Json.Serialization;
using Admin.API.Application.Interfaces;
using Marketplace.Contracts.V1;
using StackExchange.Redis;

namespace Admin.API.Infrastructure.Caching;

public sealed class RedisPricePolicyCache : IPricePolicyCache {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IConnectionMultiplexer _redis;

    public RedisPricePolicyCache(IConnectionMultiplexer redis) {
        _redis = redis;
    }

    public async Task SetAsync(PricePolicySnapshot snapshot, CancellationToken cancellationToken) {
        var db = _redis.GetDatabase();
        var key = CacheKeys.PricePolicy(snapshot.TemplateId);
        var payload = JsonSerializer.Serialize(snapshot, JsonOptions);
        await db.StringSetAsync(key, payload);
    }
}
