using Wallet.API.Domain.Enums;

namespace Wallet.API.Domain.Entities;

public sealed class TopUpOrder
{
    public const string DefaultProvider = "ZALOPAY";

    public Guid TopUpId { get; set; }
    public Guid UserId { get; set; }
    public string Provider { get; set; } = DefaultProvider;
    public string AppTransId { get; set; } = string.Empty;
    public string? PaymentUrl { get; set; }
    public long? ProviderTxnId { get; set; }
    public long AmountVnd { get; set; }
    public long GemAmount { get; set; }
    public TopUpOrderStatus Status { get; set; } = TopUpOrderStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }

    public Wallet Wallet { get; set; } = null!;
    public ICollection<WalletTransaction> Transactions { get; set; } = new List<WalletTransaction>();
}