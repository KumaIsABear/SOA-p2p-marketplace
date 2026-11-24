using Marketplace.Contracts.V1;
using MassTransit;
using Wallet.API.Application.Services;

namespace Wallet.API.Application.Consumers;

public sealed class HoldBuyerFundsConsumer : IConsumer<HoldBuyerFundsCommand>
{
    private readonly IWalletSagaService _walletSagaService;

    public HoldBuyerFundsConsumer(IWalletSagaService walletSagaService)
    {
        _walletSagaService = walletSagaService;
    }

    public async Task Consume(ConsumeContext<HoldBuyerFundsCommand> context)
    {
        var message = context.Message;
        var result = await _walletSagaService.HoldBuyerFundsAsync(message, context.CancellationToken);

        if (result.IsHeld)
        {
            await context.Publish(
                new BuyerFundsHeldEvent(
                    message.CorrelationId,
                    message.OperationId,
                    DateTimeOffset.UtcNow,
                    result.TradeId,
                    result.BuyerId,
                    result.Amount),
                sendContext => sendContext.CorrelationId = message.CorrelationId,
                context.CancellationToken);

            return;
        }

        await context.Publish(
            new BuyerFundsHoldFailedEvent(
                message.CorrelationId,
                message.OperationId,
                DateTimeOffset.UtcNow,
                result.TradeId,
                result.FailureCode!),
            sendContext => sendContext.CorrelationId = message.CorrelationId,
            context.CancellationToken);
    }
}