namespace Wallet.API.Infrastructure.Payments;

public sealed record ZaloPayCreateOrderRequest(
    string AppTransId,
    Guid UserId,
    long AppTimeMs,
    long AmountVnd,
    long GemAmount,
    string PackageId,
    string Description
);

public enum ZaloPayCreateOutcome
{
    Created,
    Rejected,
    Ambiguous
}

public sealed record ZaloPayCreateOrderResult(
    ZaloPayCreateOutcome Outcome,
    int? ReturnCode,
    int? SubReturnCode,
    string? ReturnMessage,
    string? SubReturnMessage,
    string? OrderUrl
);

public enum ZaloPayQueryOutcome
{
    Paid,
    Failed,
    Processing,
    TransientError
}

public sealed record ZaloPayQueryOrderResult(
    ZaloPayQueryOutcome Outcome,
    int? ReturnCode,
    int? SubReturnCode,
    bool IsProcessing,
    long Amount,
    long ZpTransId,
    string? ReturnMessage
);

public interface IZaloPayClient
{
    string GenerateAppTransId(Guid topUpId, DateTimeOffset utcNow);

    Task<ZaloPayCreateOrderResult> CreateOrderAsync(
        ZaloPayCreateOrderRequest request,
        CancellationToken cancellationToken);

    Task<ZaloPayQueryOrderResult> QueryOrderAsync(
        string appTransId,
        CancellationToken cancellationToken);

    bool VerifyCallbackMac(string rawData, string providedMac);
}