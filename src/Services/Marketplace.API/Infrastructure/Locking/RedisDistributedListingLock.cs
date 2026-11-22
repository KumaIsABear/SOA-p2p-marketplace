using Marketplace.API.Application.Common;
using Marketplace.API.Application.Interfaces;
using Marketplace.Contracts.V1;
using StackExchange.Redis;

namespace Marketplace.API.Infrastructure.Locking;

public sealed class RedisDistributedListingLock : IDistributedListingLock
{
    private const string CompareAndDeleteScript = """
        if redis.call("get", KEYS[1]) == ARGV[1] then
            return redis.call("del", KEYS[1])
        else
            return 0
        end
        """;

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisDistributedListingLock> _logger;

    public RedisDistributedListingLock(IConnectionMultiplexer redis, ILogger<RedisDistributedListingLock> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(Guid listingId, TimeSpan ttl, CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = CacheKeys.ListingLock(listingId);
            var token = Guid.NewGuid().ToString("N");

            var acquired = await db.StringSetAsync(key, token, ttl, When.NotExists);
            return acquired ? new LockHandle(db, key, token, _logger) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis unavailable while acquiring lock for listing {ListingId}.", listingId);
            throw new MarketplaceApiException(
                StatusCodes.Status503ServiceUnavailable,
                MarketplaceErrorCodes.DependencyUnavailable,
                "Distributed lock service is unavailable."
            );
        }
    }

    private sealed class LockHandle : IAsyncDisposable
    {
        private readonly IDatabase _db;
        private readonly string _key;
        private readonly string _token;
        private readonly ILogger _logger;

        public LockHandle(IDatabase db, string key, string token, ILogger logger)
        {
            _db = db;
            _key = key;
            _token = token;
            _logger = logger;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _db.ScriptEvaluateAsync(CompareAndDeleteScript, [_key], [_token]);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to release Redis lock key {LockKey}.", _key);
            }
        }
    }
}
