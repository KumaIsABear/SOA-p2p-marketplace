using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gateway.API.Application.DTOs;
using Gateway.API.Application.Interfaces;
using Gateway.API.Application.Options;
using Microsoft.Extensions.Options;

namespace Gateway.API.Infrastructure.Clients;

public sealed class AuthStatusClient : IAuthStatusClient {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ServiceAuthOptions _serviceAuth;
    private readonly ILogger<AuthStatusClient> _logger;

    public AuthStatusClient(
        HttpClient httpClient,
        IOptions<ServiceAuthOptions> serviceAuth,
        ILogger<AuthStatusClient> logger
    ) {
        _httpClient = httpClient;
        _serviceAuth = serviceAuth.Value;
        _logger = logger;
    }

    public async Task<AuthStatusLookupResult> GetUserStatusAsync(
        Guid userId,
        string correlationId,
        CancellationToken cancellationToken
    ) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/auth/users/{userId:D}/status");
        request.Headers.TryAddWithoutValidation("X-Service-Key", _serviceAuth.OutgoingKey);
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);

        try {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound) {
                return new AuthStatusLookupResult(AuthStatusLookupOutcome.NotFound);
            }

            if (!response.IsSuccessStatusCode) {
                _logger.LogWarning(
                    "AUTH-I01 returned HTTP {StatusCode} for user {UserId} (TraceId={TraceId}).",
                    (int)response.StatusCode,
                    userId,
                    correlationId
                );
                return new AuthStatusLookupResult(AuthStatusLookupOutcome.Unavailable);
            }

            var payload = await response.Content.ReadFromJsonAsync<AccessStatusDto>(JsonOptions, cancellationToken);
            if (payload is null) {
                _logger.LogWarning("AUTH-I01 returned empty payload for user {UserId} (TraceId={TraceId}).", userId, correlationId);
                return new AuthStatusLookupResult(AuthStatusLookupOutcome.Unavailable);
            }

            return new AuthStatusLookupResult(AuthStatusLookupOutcome.Found, payload);
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) {
            _logger.LogError(ex, "Failed to reach Auth.API for status check (TraceId={TraceId}).", correlationId);
            return new AuthStatusLookupResult(AuthStatusLookupOutcome.Unavailable);
        }
    }

    public async Task<bool> IsAuthReadyAsync(CancellationToken cancellationToken) {
        try {
            using var response = await _httpClient.GetAsync("/health/ready", cancellationToken);
            return response.IsSuccessStatusCode;
        } catch {
            return false;
        }
    }
}
