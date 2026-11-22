using Marketplace.API.Application.Interfaces;
using Marketplace.Contracts.V1;
using MassTransit;

namespace Marketplace.API.Infrastructure.Messaging;

public sealed class ListingProjectionConsumer :
    IConsumer<ListingCreatedEvent>,
    IConsumer<ListingProcessingEvent>,
    IConsumer<ListingCancelledEvent>,
    IConsumer<ListingReactivatedEvent>,
    IConsumer<TradeCompletedEvent>
{
    private readonly ICatalogProjectionStore _store;

    public ListingProjectionConsumer(ICatalogProjectionStore store)
    {
        _store = store;
    }

    public Task Consume(ConsumeContext<ListingCreatedEvent> context) =>
        _store.UpsertAsync(context.Message.Listing, context.CancellationToken);

    public Task Consume(ConsumeContext<ListingProcessingEvent> context) =>
        _store.UpsertAsync(context.Message.Listing, context.CancellationToken);

    public Task Consume(ConsumeContext<ListingCancelledEvent> context) =>
        _store.UpsertAsync(context.Message.Listing, context.CancellationToken);

    public Task Consume(ConsumeContext<ListingReactivatedEvent> context) =>
        _store.UpsertAsync(context.Message.Listing, context.CancellationToken);

    public Task Consume(ConsumeContext<TradeCompletedEvent> context) =>
        _store.UpsertAsync(context.Message.Listing, context.CancellationToken);
}
