using System.Text.Json;
using Auth.API.Application.Interfaces;
using Auth.API.Application.Options;
using Google.Apis.Auth;
using Microsoft.Extensions.Options;

namespace Auth.API.Infrastructure.Security;

public sealed class GoogleTokenValidator : IGoogleTokenValidator {
    private static readonly HashSet<string> AllowedIssuers = new(StringComparer.OrdinalIgnoreCase) {
        "accounts.google.com",
        "https://accounts.google.com"
    };

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

        if (!HasValidJwtStructure(idToken)) {
            return new GoogleValidationResult(GoogleValidationStatus.InvalidToken);
        }

        try {
            var settings = new GoogleJsonWebSignature.ValidationSettings {
                Audience = [_options.ClientId]
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
            var issuer = (payload.Issuer ?? string.Empty).Trim();
            if (!AllowedIssuers.Contains(issuer)) {
                return new GoogleValidationResult(GoogleValidationStatus.InvalidToken);
            }

            var identity = new GoogleIdentityPayload(
                issuer,
                payload.Subject ?? string.Empty,
                payload.Email ?? string.Empty,
                payload.EmailVerified,
                payload.Name ?? payload.Email ?? string.Empty,
                payload.Nonce
            );

            return new GoogleValidationResult(GoogleValidationStatus.Valid, identity);
        } catch (HttpRequestException ex) {
            _logger.LogError(ex, "Failed to reach Google JWKS endpoint.");
            return new GoogleValidationResult(GoogleValidationStatus.ServiceUnavailable);
        } catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
            _logger.LogError(ex, "Timed out while verifying Google ID token.");
            return new GoogleValidationResult(GoogleValidationStatus.ServiceUnavailable);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        } catch (Exception ex) when (IsInvalidTokenException(ex)) {
            return new GoogleValidationResult(GoogleValidationStatus.InvalidToken);
        }
    }

    private static bool HasValidJwtStructure(string token) {
        var parts = token.Split('.');
        if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace)) {
            return false;
        }

        return TryDecodeJsonObject(parts[0]) && TryDecodeJsonObject(parts[1]);
    }

    private static bool TryDecodeJsonObject(string base64UrlSegment) {
        try {
            var padded = base64UrlSegment.Replace('-', '+').Replace('_', '/');
            var mod = padded.Length % 4;
            if (mod == 1) {
                return false;
            }
            if (mod > 0) {
                padded += new string('=', 4 - mod);
            }

            var bytes = Convert.FromBase64String(padded);
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        } catch {
            return false;
        }
    }

    private static bool IsInvalidTokenException(Exception ex) {
        if (ex is InvalidJwtException or ArgumentException or FormatException or JsonException) {
            return true;
        }

        var typeName = ex.GetType().FullName;
        return typeName is not null && typeName.Contains("Json", StringComparison.OrdinalIgnoreCase);
    }
}
