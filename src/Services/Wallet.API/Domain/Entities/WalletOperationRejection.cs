using Wallet.API.Domain.Enums;

namespace Wallet.API.Domain.Entities;

public sealed class WalletOperationRejection
{
    public Guid OperationId { get; set; }
    public Guid TradeId { get; set; }
    public WalletOperationType OperationType { get; set; }
    public string FailureCode { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}