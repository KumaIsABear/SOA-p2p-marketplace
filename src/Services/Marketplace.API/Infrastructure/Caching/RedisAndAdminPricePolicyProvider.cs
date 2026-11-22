using System.Net;
using System.Text.Json;
using Marketplace.API.Application.Common;
using Marketplace.API.Application.Interfaces;
using Marketplace.API.Infrastructure.Http;
using Marketplace.Contracts.V1;
using StackExchange.Redis;

namespace Marketplace.API.Infrastructure.Caching;

public sealed class RedisAndAdminPricePolicyProvider : IPricePolicyProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer _redis;
    private readonly HttpClient _adminHttpClient;
    private readonly ILogger<RedisAndAdminPricePolicyProvider> _logger;

    public RedisAndAdminPricePolicyProvider(
        IConnectionMultiplexer redis,
        HttpClient adminHttpClient,
        ILogger<RedisAndAdminPricePolicyProvider> logger)
    {
        _redis = redis;
        _adminHttpClient = adminHttpClient;
        _logger = logger;
    }

    public async Task<PricePolicySnapshot> GetRequiredPolicyAsync(Guid templateId, CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            var raw = await db.StringGetAsync(CacheKeys.PricePolicy(templateId));
            if (raw.HasValue)
            {
                var cached = JsonSerializer.Deserialize<PricePolicySnapshot>(raw!, JsonOptions);
                if (cached is not null)
                    return cached;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis lookup failed for price policy {TemplateId}. Falling back to ADM-I01.", templateId);
        }

        try
        {
            using var response = await _adminHttpClient.GetAsync($"/internal/v1/admin/price-policies/{templateId}", ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new MarketplaceApiException(
                    StatusCodes.Status503ServiceUnavailable,
                    MarketplaceErrorCodes.PricePolicyUnavailable,
                    "Price policy is not configured for this item template."
                );
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new MarketplaceApiException(
                    StatusCodes.Status503ServiceUnavailable,
                    MarketplaceErrorCodes.PricePolicyUnavailable,
                    "Unable to retrieve price policy from Admin service."
                );
            }

            var policy = await response.Content.ReadFromJsonAsync<PricePolicySnapshot>(JsonOptions, ct);
            if (policy is null)
            {
                throw new MarketplaceApiException(
                    StatusCodes.Status503ServiceUnavailable,
                    MarketplaceErrorCodes.PricePolicyUnavailable,
                    "Invalid price policy response from Admin service."
                );
            }

            return policy;
        }
        catch (MarketplaceApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ADM-I01 fallback failed for template {TemplateId}.", templateId);
            throw new MarketplaceApiException(
                StatusCodes.Status503ServiceUnavailable,
                MarketplaceErrorCodes.PricePolicyUnavailable,
                "Price policy service is unavailable."
            );
        }
    }
}
