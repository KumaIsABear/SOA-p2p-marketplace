namespace Auth.API.Application.Interfaces;

public sealed record GoogleIdentityPayload(
    string Issuer,
    string Subject,
    string Email,
    bool EmailVerified,
    string DisplayName,
    string? Nonce
);

public enum GoogleValidationStatus {
    Valid,
    InvalidToken,
    ServiceUnavailable
}

public sealed record GoogleValidationResult(
    GoogleValidationStatus Status,
    GoogleIdentityPayload? Payload = null
);

public interface IGoogleTokenValidator {
    Task<GoogleValidationResult> ValidateAsync(string idToken, CancellationToken cancellationToken);
}
