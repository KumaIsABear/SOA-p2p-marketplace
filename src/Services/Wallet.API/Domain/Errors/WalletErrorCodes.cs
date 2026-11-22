namespace Wallet.API.Domain.Errors;

public static class WalletErrorCodes
{
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string Forbidden = "FORBIDDEN";
    public const string AccountBanned = "ACCOUNT_BANNED";
    public const string ValidationError = "VALIDATION_ERROR";
    public const string InternalError = "INTERNAL_ERROR";
    public const string DependencyUnavailable = "DEPENDENCY_UNAVAILABLE";
    public const string PackageNotFound = "PACKAGE_NOT_FOUND";
    public const string TopUpNotFound = "TOPUP_NOT_FOUND";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string PaymentCreateRejected = "PAYMENT_CREATE_REJECTED";
    public const string InsufficientFunds = "INSUFFICIENT_FUNDS";
}