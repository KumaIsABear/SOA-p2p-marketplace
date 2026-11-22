namespace Wallet.API.Application.DTOs;

public sealed record WalletSupplyResponse(
    long TotalGem,
    long HeldGem,
    long AvailableGem,
    DateTimeOffset AsOf
);