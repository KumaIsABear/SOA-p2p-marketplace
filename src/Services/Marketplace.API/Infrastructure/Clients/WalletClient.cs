using System.Text.Json;
using Marketplace.API.Application.DTOs;
using Marketplace.API.Application.Interfaces;

namespace Marketplace.API.Infrastructure.Clients;

public sealed class WalletClient : IWalletClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ILogger<WalletClient> _logger;

    public WalletClient(HttpClient http, ILogger<WalletClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<WalletTradeTraceDto?> GetTradeTraceAsync(Guid tradeId, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync($"/internal/v1/wallet/trades/{tradeId}/trace", ct);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<WalletTradeTraceDto>(JsonOptions, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WAL-I01 trade trace query failed for trade {TradeId}.", tradeId);
            return null;
        }
    }
}
