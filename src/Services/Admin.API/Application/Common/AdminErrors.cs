namespace Admin.API.Application.Common;

public static class AdminErrors {
    public static AdminError ValidationError(string detail, string[]? errors = null) =>
        new(StatusCodes.Status400BadRequest, "Bad Request", "VALIDATION_ERROR", detail, errors);

    public static AdminError Unauthenticated(string detail = "Authentication is required or the access token is invalid.") =>
        new(StatusCodes.Status401Unauthorized, "Unauthorized", "UNAUTHENTICATED", detail);

    public static AdminError Forbidden(string detail = "You do not have permission to access this resource.") =>
        new(StatusCodes.Status403Forbidden, "Forbidden", "FORBIDDEN", detail);

    public static AdminError PricePolicyNotFound() =>
        new(StatusCodes.Status404NotFound, "Not Found", "PRICE_POLICY_NOT_FOUND", "Price policy was not found for the specified template.");

    public static AdminError VersionConflict(long currentVersion) =>
        new(StatusCodes.Status409Conflict, "Conflict", "VERSION_CONFLICT", $"Expected version does not match current policy version ({currentVersion}).");

    public static AdminError IdempotencyConflict() =>
        new(StatusCodes.Status409Conflict, "Conflict", "IDEMPOTENCY_CONFLICT", "Idempotency-Key was already used with a different request payload.");

    public static AdminError InvalidPricePolicy(string detail, string[]? errors = null) =>
        new(StatusCodes.Status422UnprocessableEntity, "Unprocessable Entity", "INVALID_PRICE_POLICY", detail, errors);

    public static AdminError DependencyUnavailable(string detail) =>
        new(StatusCodes.Status503ServiceUnavailable, "Service Unavailable", "DEPENDENCY_UNAVAILABLE", detail);

    public static AdminError InternalError(string detail = "An unexpected error occurred.") =>
        new(StatusCodes.Status500InternalServerError, "Internal Server Error", "INTERNAL_ERROR", detail);
}
