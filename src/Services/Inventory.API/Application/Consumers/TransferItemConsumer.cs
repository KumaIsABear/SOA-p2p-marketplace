using Inventory.API.Application.Services;
using Marketplace.Contracts.V1;
using MassTransit;

namespace Inventory.API.Application.Consumers;

public sealed class TransferItemConsumer : IConsumer<TransferItemCommand>
{
    private readonly IInventorySagaService _sagaService;

    public TransferItemConsumer(IInventorySagaService sagaService)
    {
        _sagaService = sagaService;
    }

    public async Task Consume(ConsumeContext<TransferItemCommand> context)
    {
        var message = context.Message;
        var result = await _sagaService.HandleTransferItemAsync(message, context.CancellationToken);

        if (result.TransferredEvent is not null)
        {
            await context.Publish(
                result.TransferredEvent,
                sendContext => sendContext.CorrelationId = message.CorrelationId,
                context.CancellationToken);
            return;
        }

        if (result.FailedEvent is not null)
        {
            await context.Publish(
                result.FailedEvent,
                sendContext => sendContext.CorrelationId = message.CorrelationId,
                context.CancellationToken);
        }
    }
}