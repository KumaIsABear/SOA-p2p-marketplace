namespace Marketplace.API.Domain.Enums;

public enum TradeStatus
{
    HoldingFunds,
    Transferring,
    Settling,
    Finalizing,
    ReversingItem,
    ReleasingFunds,
    Completed,
    Failed
}
