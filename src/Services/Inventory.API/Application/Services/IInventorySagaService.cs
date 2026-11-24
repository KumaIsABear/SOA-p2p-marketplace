using Marketplace.Contracts.V1;

namespace Inventory.API.Application.Services;

public sealed record TransferItemSagaResult(
    ItemTransferredEvent? TransferredEvent,
    ItemTransferFailedEvent? FailedEvent)
{
    public static TransferItemSagaResult Succeeded(ItemTransferredEvent transferredEvent) =>
        new(transferredEvent, null);

    public static TransferItemSagaResult Failed(ItemTransferFailedEvent failedEvent) =>
        new(null, failedEvent);
}

public interface IInventorySagaService
{
    Task<TransferItemSagaResult> HandleTransferItemAsync(
        TransferItemCommand command,
        CancellationToken cancellationToken = default);

    Task<ItemTransferFinalizedEvent> HandleFinalizeItemTransferAsync(
        FinalizeItemTransferCommand command,
        CancellationToken cancellationToken = default);

    Task<ItemTransferReversedEvent> HandleReverseItemTransferAsync(
        ReverseItemTransferCommand command,
        CancellationToken cancellationToken = default);

    Task<ItemUnlockedEvent> HandleUnlockItemAsync(
        UnlockItemCommand command,
        CancellationToken cancellationToken = default);
}