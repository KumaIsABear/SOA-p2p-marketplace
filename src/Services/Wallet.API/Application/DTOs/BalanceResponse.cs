namespace Wallet.API.Application.DTOs;

public sealed record BalanceResponse(
    Guid UserId,
    string Currency,
    long TotalBalance,
    long HoldBalance,
    long AvailableBalance,
    long Version
);