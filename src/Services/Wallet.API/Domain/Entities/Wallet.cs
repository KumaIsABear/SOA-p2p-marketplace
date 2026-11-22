namespace Wallet.API.Domain.Entities;

public sealed class Wallet
{
    public Guid UserId { get; set; }
    public long TotalBalance { get; set; }
    public long HoldBalance { get; set; }
    public long Version { get; set; } = 1;

    public long AvailableBalance => checked(TotalBalance - HoldBalance);

    public ICollection<FundHold> FundHolds { get; set; } = new List<FundHold>();
    public ICollection<WalletTransaction> Transactions { get; set; } = new List<WalletTransaction>();
    public ICollection<TopUpOrder> TopUpOrders { get; set; } = new List<TopUpOrder>();
}