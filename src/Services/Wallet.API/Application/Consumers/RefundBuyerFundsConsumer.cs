using Marketplace.Contracts.V1;
using MassTransit;
using Wallet.API.Application.Services;

namespace Wallet.API.Application.Consumers;

public sealed class RefundBuyerFundsConsumer : IConsumer<RefundBuyerFundsCommand>
{
    private readonly IWalletSagaService _walletSagaService;

    public RefundBuyerFundsConsumer(IWalletSagaService walletSagaService)
    {
        _walletSagaService = walletSagaService;
    }

    public async Task Consume(ConsumeContext<RefundBuyerFundsCommand> context)
    {
        var message = context.Message;
        var result = await _walletSagaService.RefundBuyerFundsAsync(message, context.CancellationToken);

        await context.Publish(
            new BuyerFundsRefundedEvent(
                message.CorrelationId,
                message.OperationId,
                DateTimeOffset.UtcNow,
                result.TradeId,
                result.Amount),
            sendContext => sendContext.CorrelationId = message.CorrelationId,
            context.CancellationToken);
    }
}