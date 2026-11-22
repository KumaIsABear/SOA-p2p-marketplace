namespace Auth.API.Application.Common;

public sealed record AuthError(
    int StatusCode,
    string Title,
    string Code,
    string Detail,
    string[]? Errors = null
);

public sealed class AuthResult<T> {
    public bool IsSuccess { get; }
    public T? Value { get; }
    public AuthError? Error { get; }

    private AuthResult(T value) {
        IsSuccess = true;
        Value = value;
    }

    private AuthResult(AuthError error) {
        IsSuccess = false;
        Error = error;
    }

    public static AuthResult<T> Success(T value) => new(value);
    public static AuthResult<T> Failure(AuthError error) => new(error);
}

public static class AuthErrors {
    public static AuthError ValidationError(string detail, string[]? errors = null) =>
        new(StatusCodes.Status400BadRequest, "Bad Request", "VALIDATION_ERROR", detail, errors);

    public static AuthError Unauthenticated(string detail = "Authentication is required or the access token is invalid.") =>
        new(StatusCodes.Status401Unauthorized, "Unauthorized", "UNAUTHENTICATED", detail);

    public static AuthError InvalidCredentials() =>
        new(StatusCodes.Status401Unauthorized, "Unauthorized", "INVALID_CREDENTIALS", "Invalid email or password.");

    public static AuthError InvalidGoogleToken() =>
        new(StatusCodes.Status401Unauthorized, "Unauthorized", "INVALID_GOOGLE_TOKEN", "Google ID token is invalid or expired.");

    public static AuthError InvalidRefreshToken() =>
        new(StatusCodes.Status401Unauthorized, "Unauthorized", "INVALID_REFRESH_TOKEN", "Refresh token is invalid, expired, or revoked.");

    public static AuthError Forbidden(string detail = "You do not have permission to access this resource.") =>
        new(StatusCodes.Status403Forbidden, "Forbidden", "FORBIDDEN", detail);

    public static AuthError AccountBanned() =>
        new(StatusCodes.Status403Forbidden, "Forbidden", "ACCOUNT_BANNED", "This account is banned.");

    public static AuthError UserNotFound() =>
        new(StatusCodes.Status404NotFound, "Not Found", "USER_NOT_FOUND", "The specified user was not found.");

    public static AuthError IdentityConflict() =>
        new(StatusCodes.Status409Conflict, "Conflict", "IDENTITY_CONFLICT", "An account with this email already exists and cannot be auto-linked.");

    public static AuthError IdempotencyConflict() =>
        new(StatusCodes.Status409Conflict, "Conflict", "IDEMPOTENCY_CONFLICT", "OperationId was already used with a different request payload.");

    public static AuthError InvalidAccountTarget() =>
        new(StatusCodes.Status409Conflict, "Conflict", "INVALID_ACCOUNT_TARGET", "Only Player accounts can be moderated.");

    public static AuthError DependencyUnavailable(string detail) =>
        new(StatusCodes.Status503ServiceUnavailable, "Service Unavailable", "DEPENDENCY_UNAVAILABLE", detail);
}
