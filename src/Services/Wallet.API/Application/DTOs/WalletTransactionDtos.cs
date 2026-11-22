using Wallet.API.Domain.Enums;

namespace Wallet.API.Application.DTOs;

public sealed class WalletTransactionQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public WalletTransactionType? Type { get; init; }
    public Guid? TradeId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}

public sealed record WalletTransactionDto(
    Guid EntryId,
    Guid UserId,
    Guid? TradeId,
    Guid? TopUpId,
    long TotalDelta,
    long HoldDelta,
    long? FeeGem,
    WalletTransactionType Type,
    DateTimeOffset CreatedAt
);

public sealed record WalletTransactionPageResponse(
    IReadOnlyList<WalletTransactionDto> Items,
    int Page,
    int PageSize,
    long TotalCount
);