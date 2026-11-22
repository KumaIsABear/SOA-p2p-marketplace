using Wallet.API.Domain.Enums;

namespace Wallet.API.Application.DTOs;

public sealed record FundHoldDto(
    Guid TradeId,
    Guid BuyerId,
    long Amount,
    FundHoldStatus Status,
    DateTimeOffset UpdatedAt
);

public sealed record WalletTradeTraceResponse(
    Guid TradeId,
    FundHoldDto? Hold,
    SettlementStatus SettlementStatus,
    IReadOnlyList<WalletTransactionDto> Entries
);