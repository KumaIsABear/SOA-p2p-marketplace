using System.Text.Json;
using Admin.API.Application.DTOs;
using Admin.API.Application.Interfaces;
using Admin.API.Application.Options;
using Microsoft.Extensions.Options;

namespace Admin.API.Infrastructure.Clients;

public sealed class WalletInternalClient : IWalletInternalClient {
    private readonly HttpClient _httpClient;
    private readonly ServiceAuthOptions _serviceAuth;
    private readonly ILogger<WalletInternalClient> _logger;

    public WalletInternalClient(
        HttpClient httpClient,
        IOptions<ServiceAuthOptions> serviceAuth,
        ILogger<WalletInternalClient> logger
    ) {
        _httpClient = httpClient;
        _serviceAuth = serviceAuth.Value;
        _logger = logger;
    }

    public async Task<InternalCallResult<WalletSupplyDto>> GetSupplyAsync(
        string correlationId,
        CancellationToken cancellationToken
    ) {
        using var message = new HttpRequestMessage(HttpMethod.Get, "/internal/v1/wallet/supply");
        message.Headers.TryAddWithoutValidation("X-Service-Key", _serviceAuth.OutgoingKey);
        message.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);

        try {
            using var response = await _httpClient.SendAsync(message, cancellationToken);
            return await InternalHttpHelper.ReadResponseAsync<WalletSupplyDto>(response, cancellationToken);
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) {
            _logger.LogWarning(ex, "WAL-I02 supply call failed (TraceId={TraceId}).", correlationId);
            return new InternalCallResult<WalletSupplyDto>(InternalCallOutcome.Unavailable);
        }
    }

    public async Task<InternalCallResult<WalletTradeTraceDto>> GetTradeTraceAsync(
        Guid tradeId,
        string correlationId,
        CancellationToken cancellationToken
    ) {
        using var message = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/wallet/trades/{tradeId:D}/trace");
        message.Headers.TryAddWithoutValidation("X-Service-Key", _serviceAuth.OutgoingKey);
        message.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);

        try {
            using var response = await _httpClient.SendAsync(message, cancellationToken);
            return await InternalHttpHelper.ReadResponseAsync<WalletTradeTraceDto>(response, cancellationToken);
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) {
            _logger.LogWarning(ex, "WAL-I01 trace call failed for TradeId={TradeId} (TraceId={TraceId}).", tradeId, correlationId);
            return new InternalCallResult<WalletTradeTraceDto>(InternalCallOutcome.Unavailable);
        }
    }
}
