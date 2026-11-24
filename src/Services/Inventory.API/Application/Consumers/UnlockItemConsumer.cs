using Inventory.API.Application.Services;
using Marketplace.Contracts.V1;
using MassTransit;

namespace Inventory.API.Application.Consumers;

public sealed class UnlockItemConsumer : IConsumer<UnlockItemCommand>
{
    private readonly IInventorySagaService _sagaService;

    public UnlockItemConsumer(IInventorySagaService sagaService)
    {
        _sagaService = sagaService;
    }

    public async Task Consume(ConsumeContext<UnlockItemCommand> context)
    {
        var message = context.Message;
        var unlockedEvent = await _sagaService.HandleUnlockItemAsync(message, context.CancellationToken);

        await context.Publish(
            unlockedEvent,
            sendContext => sendContext.CorrelationId = message.CorrelationId,
            context.CancellationToken);
    }
}