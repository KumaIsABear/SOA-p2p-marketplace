using System.Net.Http.Json;
using System.Text.Json;
using Admin.API.Application.DTOs;
using Admin.API.Application.Interfaces;
using Admin.API.Application.Options;
using Microsoft.Extensions.Options;

namespace Admin.API.Infrastructure.Clients;

public sealed class AuthInternalClient : IAuthInternalClient {
    private readonly HttpClient _httpClient;
    private readonly ServiceAuthOptions _serviceAuth;
    private readonly ILogger<AuthInternalClient> _logger;

    public AuthInternalClient(
        HttpClient httpClient,
        IOptions<ServiceAuthOptions> serviceAuth,
        ILogger<AuthInternalClient> logger
    ) {
        _httpClient = httpClient;
        _serviceAuth = serviceAuth.Value;
        _logger = logger;
    }

    public async Task<InternalCallResult<AccessStatusDto>> UpdateUserStatusAsync(
        Guid userId,
        InternalModerationRequest request,
        string correlationId,
        CancellationToken cancellationToken
    ) {
        using var message = new HttpRequestMessage(HttpMethod.Patch, $"/internal/v1/auth/users/{userId:D}/status") {
            Content = JsonContent.Create(request, options: InternalHttpHelper.JsonOptions)
        };
        message.Headers.TryAddWithoutValidation("X-Service-Key", _serviceAuth.OutgoingKey);
        message.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);

        try {
            using var response = await _httpClient.SendAsync(message, cancellationToken);
            return await InternalHttpHelper.ReadResponseAsync<AccessStatusDto>(response, cancellationToken);
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) {
            _logger.LogWarning(ex, "AUTH-I02 call failed for UserId={UserId} (TraceId={TraceId}).", userId, correlationId);
            return new InternalCallResult<AccessStatusDto>(InternalCallOutcome.Unavailable);
        }
    }
}
