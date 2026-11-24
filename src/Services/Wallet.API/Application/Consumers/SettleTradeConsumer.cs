using Marketplace.Contracts.V1;
using MassTransit;
using Wallet.API.Application.Services;

namespace Wallet.API.Application.Consumers;

public sealed class SettleTradeConsumer : IConsumer<SettleTradeCommand>
{
    private readonly IWalletSagaService _walletSagaService;

    public SettleTradeConsumer(IWalletSagaService walletSagaService)
    {
        _walletSagaService = walletSagaService;
    }

    public async Task Consume(ConsumeContext<SettleTradeCommand> context)
    {
        var message = context.Message;
        var result = await _walletSagaService.SettleTradeAsync(message, context.CancellationToken);

        if (result.IsSettled)
        {
            await context.Publish(
                new TradeSettledEvent(
                    message.CorrelationId,
                    message.OperationId,
                    DateTimeOffset.UtcNow,
                    result.TradeId,
                    result.GrossGem,
                    result.FeeGem,
                    result.SellerNetGem),
                sendContext => sendContext.CorrelationId = message.CorrelationId,
                context.CancellationToken);

            return;
        }

        await context.Publish(
            new TradeSettlementRejectedEvent(
                message.CorrelationId,
                message.OperationId,
                DateTimeOffset.UtcNow,
                result.TradeId,
                result.FailureCode!),
            sendContext => sendContext.CorrelationId = message.CorrelationId,
            context.CancellationToken);
    }
}