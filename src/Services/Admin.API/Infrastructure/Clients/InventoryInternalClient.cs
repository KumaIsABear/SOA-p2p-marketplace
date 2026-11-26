using System.Text.Json;
using Admin.API.Application.DTOs;
using Admin.API.Application.Interfaces;
using Admin.API.Application.Options;
using Microsoft.Extensions.Options;

namespace Admin.API.Infrastructure.Clients;

public sealed class InventoryInternalClient : IInventoryInternalClient {
    private readonly HttpClient _httpClient;
    private readonly ServiceAuthOptions _serviceAuth;
    private readonly ILogger<InventoryInternalClient> _logger;

    public InventoryInternalClient(
        HttpClient httpClient,
        IOptions<ServiceAuthOptions> serviceAuth,
        ILogger<InventoryInternalClient> logger
    ) {
        _httpClient = httpClient;
        _serviceAuth = serviceAuth.Value;
        _logger = logger;
    }

    public async Task<InternalCallResult<ItemTransferTraceDto>> GetTradeTransferTraceAsync(
        Guid tradeId,
        string correlationId,
        CancellationToken cancellationToken
    ) {
        using var message = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/inventory/trades/{tradeId:D}/trace");
        message.Headers.TryAddWithoutValidation("X-Service-Key", _serviceAuth.OutgoingKey);
        message.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);

        try {
            using var response = await _httpClient.SendAsync(message, cancellationToken);
            return await InternalHttpHelper.ReadResponseAsync<ItemTransferTraceDto>(response, cancellationToken);
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) {
            _logger.LogWarning(ex, "INV-I06 trace call failed for TradeId={TradeId} (TraceId={TraceId}).", tradeId, correlationId);
            return new InternalCallResult<ItemTransferTraceDto>(InternalCallOutcome.Unavailable);
        }
    }
}
