using Inventory.API.Application.Services;
using Marketplace.Contracts.V1;
using MassTransit;

namespace Inventory.API.Application.Consumers;

public sealed class FinalizeItemTransferConsumer : IConsumer<FinalizeItemTransferCommand>
{
    private readonly IInventorySagaService _sagaService;

    public FinalizeItemTransferConsumer(IInventorySagaService sagaService)
    {
        _sagaService = sagaService;
    }

    public async Task Consume(ConsumeContext<FinalizeItemTransferCommand> context)
    {
        var message = context.Message;
        var finalizedEvent = await _sagaService.HandleFinalizeItemTransferAsync(message, context.CancellationToken);

        await context.Publish(
            finalizedEvent,
            sendContext => sendContext.CorrelationId = message.CorrelationId,
            context.CancellationToken);
    }
}