using Inventory.API.Application.Services;
using Marketplace.Contracts.V1;
using MassTransit;

namespace Inventory.API.Application.Consumers;

public sealed class ReverseItemTransferConsumer : IConsumer<ReverseItemTransferCommand>
{
    private readonly IInventorySagaService _sagaService;

    public ReverseItemTransferConsumer(IInventorySagaService sagaService)
    {
        _sagaService = sagaService;
    }

    public async Task Consume(ConsumeContext<ReverseItemTransferCommand> context)
    {
        var message = context.Message;
        var reversedEvent = await _sagaService.HandleReverseItemTransferAsync(message, context.CancellationToken);

        await context.Publish(
            reversedEvent,
            sendContext => sendContext.CorrelationId = message.CorrelationId,
            context.CancellationToken);
    }
}