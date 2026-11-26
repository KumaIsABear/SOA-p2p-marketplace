using System.Globalization;
using System.Text.Json;
using Admin.API.Application.DTOs;
using Admin.API.Application.Interfaces;
using Admin.API.Application.Options;
using Microsoft.Extensions.Options;

namespace Admin.API.Infrastructure.Clients;

public sealed class MarketplaceInternalClient : IMarketplaceInternalClient {
    private readonly HttpClient _httpClient;
    private readonly ServiceAuthOptions _serviceAuth;
    private readonly ILogger<MarketplaceInternalClient> _logger;

    public MarketplaceInternalClient(
        HttpClient httpClient,
        IOptions<ServiceAuthOptions> serviceAuth,
        ILogger<MarketplaceInternalClient> logger
    ) {
        _httpClient = httpClient;
        _serviceAuth = serviceAuth.Value;
        _logger = logger;
    }

    public async Task<InternalCallResult<MarketListingStatsDto>> GetStatsAsync(
        string correlationId,
        CancellationToken cancellationToken
    ) {
        using var message = new HttpRequestMessage(HttpMethod.Get, "/internal/v1/marketplace/stats");
        message.Headers.TryAddWithoutValidation("X-Service-Key", _serviceAuth.OutgoingKey);
        message.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);

        try {
            using var response = await _httpClient.SendAsync(message, cancellationToken);
            return await InternalHttpHelper.ReadResponseAsync<MarketListingStatsDto>(response, cancellationToken);
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) {
            _logger.LogWarning(ex, "MKT-I03 stats call failed (TraceId={TraceId}).", correlationId);
            return new InternalCallResult<MarketListingStatsDto>(InternalCallOutcome.Unavailable);
        }
    }

    public async Task<InternalCallResult<MarketTradeTraceDto>> GetTradeTraceAsync(
        Guid tradeId,
        string correlationId,
        CancellationToken cancellationToken
    ) {
        using var message = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/marketplace/trades/{tradeId:D}/trace");
        message.Headers.TryAddWithoutValidation("X-Service-Key", _serviceAuth.OutgoingKey);
        message.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);

        try {
            using var response = await _httpClient.SendAsync(message, cancellationToken);
            return await InternalHttpHelper.ReadResponseAsync<MarketTradeTraceDto>(response, cancellationToken);
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) {
            _logger.LogWarning(ex, "MKT-I01 trace call failed for TradeId={TradeId} (TraceId={TraceId}).", tradeId, correlationId);
            return new InternalCallResult<MarketTradeTraceDto>(InternalCallOutcome.Unavailable);
        }
    }

    public async Task<InternalCallResult<TradePageDto>> SearchTradesAsync(
        Guid userId,
        string? status,
        int page,
        int pageSize,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string correlationId,
        CancellationToken cancellationToken
    ) {
        var queryParts = new List<string> {
            $"userId={Uri.EscapeDataString(userId.ToString("D"))}",
            $"page={page.ToString(CultureInfo.InvariantCulture)}",
            $"pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}"
        };

        if (!string.IsNullOrWhiteSpace(status)) {
            queryParts.Add($"status={Uri.EscapeDataString(status)}");
        }

        if (from.HasValue) {
            queryParts.Add($"from={Uri.EscapeDataString(from.Value.ToUniversalTime().ToString("O"))}");
        }

        if (to.HasValue) {
            queryParts.Add($"to={Uri.EscapeDataString(to.Value.ToUniversalTime().ToString("O"))}");
        }

        var uri = $"/internal/v1/marketplace/trades?{string.Join("&", queryParts)}";
        using var message = new HttpRequestMessage(HttpMethod.Get, uri);
        message.Headers.TryAddWithoutValidation("X-Service-Key", _serviceAuth.OutgoingKey);
        message.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);

        try {
            using var response = await _httpClient.SendAsync(message, cancellationToken);
            return await InternalHttpHelper.ReadResponseAsync<TradePageDto>(response, cancellationToken);
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) {
            _logger.LogWarning(ex, "MKT-I02 search call failed for UserId={UserId} (TraceId={TraceId}).", userId, correlationId);
            return new InternalCallResult<TradePageDto>(InternalCallOutcome.Unavailable);
        }
    }
}
