using System.Net;
using System.Text.Json;
using Marketplace.API.Application.Common;
using Marketplace.API.Application.DTOs;
using Marketplace.API.Application.Interfaces;

namespace Marketplace.API.Infrastructure.Clients;

public sealed class InventoryClient : IInventoryClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ILogger<InventoryClient> _logger;

    public InventoryClient(HttpClient http, ILogger<InventoryClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<InventoryItemDto?> GetItemAsync(Guid itemId, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync($"/internal/v1/inventory/items/{itemId}", ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
                throw new MarketplaceApiException(503, MarketplaceErrorCodes.DependencyUnavailable, "Inventory service failed to fetch item.");

            return await response.Content.ReadFromJsonAsync<InventoryItemDto>(JsonOptions, ct);
        }
        catch (MarketplaceApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "INV-I02 failed for item {ItemId}.", itemId);
            throw new MarketplaceApiException(503, MarketplaceErrorCodes.DependencyUnavailable, "Inventory service is unavailable.");
        }
    }

    public async Task<LockCallResult> LockItemAsync(LockItemRequestDto request, CancellationToken ct)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync("/internal/v1/inventory/lock", request, JsonOptions, ct);

            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<LockItemResultDto>(JsonOptions, ct);
                return data is not null
                    ? new LockCallResult(LockCallOutcomeType.Succeeded, Data: data)
                    : new LockCallResult(LockCallOutcomeType.UnknownFailure);
            }

            if ((int)response.StatusCode is >= 400 and < 500)
            {
                var problem = await TryReadProblemAsync(response, ct);
                return new LockCallResult(
                    LockCallOutcomeType.BusinessRejected,
                    StatusCode: (int)response.StatusCode,
                    ErrorCode: problem?.Code ?? MarketplaceErrorCodes.ItemLocked,
                    ErrorDetail: problem?.Detail ?? "Inventory rejected the lock request."
                );
            }

            return new LockCallResult(LockCallOutcomeType.UnknownFailure);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "INV-I03 returned unknown outcome for listing {ListingId}.", request.ListingId);
            return new LockCallResult(LockCallOutcomeType.UnknownFailure);
        }
    }

    public async Task<bool> TryUnlockItemAsync(Guid listingId, UnlockItemRequestDto request, CancellationToken ct)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync($"/internal/v1/inventory/locks/{listingId}/unlock", request, JsonOptions, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "INV-I04 compensation unlock failed for listing {ListingId}.", listingId);
            return false;
        }
    }

    public async Task<ItemLockStatusDto?> GetLockStatusAsync(Guid listingId, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync($"/internal/v1/inventory/locks/{listingId}", ct);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<ItemLockStatusDto>(JsonOptions, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "INV-I05 lock status query failed for listing {ListingId}.", listingId);
            return null;
        }
    }

    public async Task<ItemTransferTraceDto?> GetTransferTraceAsync(Guid tradeId, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync($"/internal/v1/inventory/trades/{tradeId}/trace", ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<ItemTransferTraceDto>(JsonOptions, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "INV-I06 transfer trace query failed for trade {TradeId}.", tradeId);
            return null;
        }
    }

    private static async Task<ApiProblemDetails?> TryReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ApiProblemDetails>(JsonOptions, ct);
        }
        catch
        {
            return null;
        }
    }
}
