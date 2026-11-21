using Auth.API.Application.Interfaces;
using Auth.API.Application.Options;
using Google.Apis.Auth;
using Microsoft.Extensions.Options;

namespace Auth.API.Infrastructure.Security;

public sealed class GoogleTokenValidator : IGoogleTokenValidator {
    private readonly GoogleAuthOptions _options;
    private readonly ILogger<GoogleTokenValidator> _logger;

    public GoogleTokenValidator(IOptions<GoogleAuthOptions> options, ILogger<GoogleTokenValidator> logger) {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GoogleValidationResult> ValidateAsync(string idToken, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(_options.ClientId)) {
            _logger.LogWarning("Google login attempted while Google:ClientId is not configured.");
            return new GoogleValidationResult(GoogleValidationStatus.ServiceUnavailable);
        }

        try {
            var settings = new GoogleJsonWebSignature.ValidationSettings {
                Audience = [_options.ClientId]
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
            var identity = new GoogleIdentityPayload(
                payload.Issuer ?? string.Empty,
                payload.Subject ?? string.Empty,
                payload.Email ?? string.Empty,
                payload.EmailVerified,
                payload.Name ?? payload.Email ?? string.Empty,
                payload.Nonce
            );

            return new GoogleValidationResult(GoogleValidationStatus.Valid, identity);
        } catch (InvalidJwtException) {
            return new GoogleValidationResult(GoogleValidationStatus.InvalidToken);
        } catch (HttpRequestException ex) {
            _logger.LogError(ex, "Failed to reach Google JWKS endpoint.");
            return new GoogleValidationResult(GoogleValidationStatus.ServiceUnavailable);
        } catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
            _logger.LogError(ex, "Timed out while verifying Google ID token.");
            return new GoogleValidationResult(GoogleValidationStatus.ServiceUnavailable);
        }
    }
}
