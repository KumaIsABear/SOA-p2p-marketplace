using Wallet.API.Domain.Enums;

namespace Wallet.API.Domain.Entities;

public sealed class FundHold
{
    public Guid TradeId { get; set; }
    public Guid BuyerId { get; set; }
    public long Amount { get; set; }
    public FundHoldStatus Status { get; set; } = FundHoldStatus.Held;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Wallet BuyerWallet { get; set; } = null!;
}