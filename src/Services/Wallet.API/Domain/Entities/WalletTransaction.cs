using Wallet.API.Domain.Enums;

namespace Wallet.API.Domain.Entities;

public sealed class WalletTransaction
{
    public Guid EntryId { get; set; }
    public Guid UserId { get; set; }
    public string OperationKey { get; set; } = string.Empty;
    public Guid? TradeId { get; set; }
    public Guid? TopUpId { get; set; }
    public long TotalDelta { get; set; }
    public long HoldDelta { get; set; }
    public long? FeeGem { get; set; }
    public WalletTransactionType Type { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Wallet Wallet { get; set; } = null!;
    public TopUpOrder? TopUpOrder { get; set; }
}