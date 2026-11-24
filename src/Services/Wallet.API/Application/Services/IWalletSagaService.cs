using Marketplace.Contracts.V1;

namespace Wallet.API.Application.Services;

public sealed record HoldBuyerFundsResult(
    bool IsHeld,
    Guid TradeId,
    Guid BuyerId,
    long Amount,
    string? FailureCode)
{
    public static HoldBuyerFundsResult Held(Guid tradeId, Guid buyerId, long amount) =>
        new(true, tradeId, buyerId, amount, null);

    public static HoldBuyerFundsResult Rejected(Guid tradeId, Guid buyerId, long amount, string failureCode) =>
        new(false, tradeId, buyerId, amount, failureCode);
}

public sealed record SettleTradeResult(
    bool IsSettled,
    Guid TradeId,
    long GrossGem,
    long FeeGem,
    long SellerNetGem,
    string? FailureCode)
{
    public static SettleTradeResult Settled(Guid tradeId, long grossGem, long feeGem, long sellerNetGem) =>
        new(true, tradeId, grossGem, feeGem, sellerNetGem, null);

    public static SettleTradeResult Rejected(Guid tradeId, string failureCode) =>
        new(false, tradeId, 0L, 0L, 0L, failureCode);
}

public sealed record RefundBuyerFundsResult(
    Guid TradeId,
    long Amount);

public interface IWalletSagaService
{
    Task<HoldBuyerFundsResult> HoldBuyerFundsAsync(
        HoldBuyerFundsCommand command,
        CancellationToken cancellationToken);

    Task<SettleTradeResult> SettleTradeAsync(
        SettleTradeCommand command,
        CancellationToken cancellationToken);

    Task<RefundBuyerFundsResult> RefundBuyerFundsAsync(
        RefundBuyerFundsCommand command,
        CancellationToken cancellationToken);
}