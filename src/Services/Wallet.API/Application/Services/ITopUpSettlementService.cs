using Wallet.API.Domain.Enums;

namespace Wallet.API.Application.Services;

public enum TopUpSettlementOutcome
{
    Settled,
    Duplicate,
    OrderNotFound,
    Discrepancy
}

public sealed record TopUpSettlementRequest(
    string AppTransId,
    long ZpTransId,
    long AmountVnd,
    Guid? ExpectedUserId,
    string Source
);

public sealed record TopUpSettlementResult(
    TopUpSettlementOutcome Outcome,
    Guid? TopUpId,
    string Reason
);

public interface ITopUpSettlementService
{
    Task<TopUpSettlementResult> SettleTopUpAsync(
        TopUpSettlementRequest request,
        CancellationToken cancellationToken);

    Task<bool> MarkTerminalFailureAsync(
        Guid topUpId,
        TopUpOrderStatus terminalStatus,
        CancellationToken cancellationToken);
}