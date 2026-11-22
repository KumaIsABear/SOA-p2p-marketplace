using System.Net;
using System.Text.Json;
using Marketplace.API.Application.Common;
using Marketplace.API.Application.DTOs;
using Marketplace.API.Application.Interfaces;

namespace Marketplace.API.Infrastructure.Clients;

public sealed class AuthStatusClient : IAuthStatusClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ILogger<AuthStatusClient> _logger;

    public AuthStatusClient(HttpClient http, ILogger<AuthStatusClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<AuthAccessStatusDto> GetAccessStatusAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync($"/internal/v1/auth/users/{userId}/status", ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new MarketplaceApiException(401, MarketplaceErrorCodes.Unauthenticated, "User account was not found.");

            if (!response.IsSuccessStatusCode)
            {
                throw new MarketplaceApiException(
                    503,
                    MarketplaceErrorCodes.DependencyUnavailable,
                    "Unable to verify account status."
                );
            }

            var dto = await response.Content.ReadFromJsonAsync<AuthAccessStatusDto>(JsonOptions, ct);
            return dto ?? throw new MarketplaceApiException(503, MarketplaceErrorCodes.DependencyUnavailable, "Invalid Auth status response.");
        }
        catch (MarketplaceApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AUTH-I01 check failed for user {UserId}. Failing closed.", userId);
            throw new MarketplaceApiException(503, MarketplaceErrorCodes.DependencyUnavailable, "Account verification service is unavailable.");
        }
    }
}
