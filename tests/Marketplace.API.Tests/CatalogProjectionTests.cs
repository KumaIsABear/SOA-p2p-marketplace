using Marketplace.API.Application.DTOs;
using Marketplace.API.Application.Interfaces;
using Marketplace.API.Domain.Entities;
using Marketplace.API.Domain.Services;
using Marketplace.API.Infrastructure.Messaging;
using Marketplace.Contracts.V1;
using MassTransit;
using NSubstitute;

namespace Marketplace.API.Tests;

public sealed class CatalogProjectionTests
{
    [Fact]
    public void UT05_ProjectionGuard_V1Active_V3Sold_RejectsStaleV2Processing_V1Active_AndPostTombstoneUpdates()
    {
        var listingId = Guid.NewGuid();

        var r1 = CatalogProjectionGuard.Apply(null, CreateSnapshot(listingId, 1, ListingStatus.Active));
        Assert.True(r1.Applied);
        Assert.Equal(1, r1.State.Version);
        Assert.Equal(ListingStatus.Active, r1.State.Status);

        var r3 = CatalogProjectionGuard.Apply(r1.State, CreateSnapshot(listingId, 3, ListingStatus.Sold));
        Assert.True(r3.Applied);
        Assert.Equal(3, r3.State.Version);
        Assert.Equal(ListingStatus.Sold, r3.State.Status);

        var staleProcessing = CatalogProjectionGuard.Apply(r3.State, CreateSnapshot(listingId, 2, ListingStatus.Processing));
        Assert.False(staleProcessing.Applied);
        Assert.Equal(3, staleProcessing.State.Version);
        Assert.Equal(ListingStatus.Sold, staleProcessing.State.Status);

        var staleActive = CatalogProjectionGuard.Apply(staleProcessing.State, CreateSnapshot(listingId, 1, ListingStatus.Active));
        Assert.False(staleActive.Applied);
        Assert.Equal(3, staleActive.State.Version);
        Assert.Equal(ListingStatus.Sold, staleActive.State.Status);

        var duplicateSold = CatalogProjectionGuard.Apply(staleActive.State, CreateSnapshot(listingId, 3, ListingStatus.Sold));
        Assert.False(duplicateSold.Applied);
        Assert.Equal(3, duplicateSold.State.Version);
        Assert.Equal(ListingStatus.Sold, duplicateSold.State.Status);

        var resurrectAttempt = CatalogProjectionGuard.Apply(duplicateSold.State, CreateSnapshot(listingId, 4, ListingStatus.Active));
        Assert.False(resurrectAttempt.Applied);
        Assert.Equal(3, resurrectAttempt.State.Version);
        Assert.Equal(ListingStatus.Sold, resurrectAttempt.State.Status);
    }

    [Fact]
    public void UT05_ProjectionGuard_CancelledTombstone_PreventsResurrection()
    {
        var listingId = Guid.NewGuid();

        var created = CatalogProjectionGuard.Apply(null, CreateSnapshot(listingId, 1, ListingStatus.Active));
        var cancelled = CatalogProjectionGuard.Apply(created.State, CreateSnapshot(listingId, 2, ListingStatus.Cancelled));
        var resurrect = CatalogProjectionGuard.Apply(cancelled.State, CreateSnapshot(listingId, 3, ListingStatus.Active));

        Assert.True(cancelled.Applied);
        Assert.Equal(ListingStatus.Cancelled, cancelled.State.Status);
        Assert.False(resurrect.Applied);
        Assert.Equal(2, resurrect.State.Version);
        Assert.Equal(ListingStatus.Cancelled, resurrect.State.Status);
    }

    [Fact]
    public void UT05_ProjectionGuard_NonTerminalListing_AllowsReactivationWithHigherVersion()
    {
        var listingId = Guid.NewGuid();

        var active = CatalogProjectionGuard.Apply(null, CreateSnapshot(listingId, 1, ListingStatus.Active));
        var processing = CatalogProjectionGuard.Apply(active.State, CreateSnapshot(listingId, 2, ListingStatus.Processing));
        var reactivated = CatalogProjectionGuard.Apply(processing.State, CreateSnapshot(listingId, 3, ListingStatus.Active));

        Assert.True(processing.Applied);
        Assert.Equal(ListingStatus.Processing, processing.State.Status);
        Assert.True(reactivated.Applied);
        Assert.Equal(3, reactivated.State.Version);
        Assert.Equal(ListingStatus.Active, reactivated.State.Status);
    }

    [Fact]
    public async Task UT05_ListingProjectionConsumer_RoutesEventsThroughVersionAndTombstoneGuard()
    {
        var listingId = Guid.NewGuid();
        var states = new Dictionary<Guid, CatalogProjectionState>();

        var store = Substitute.For<ICatalogProjectionStore>();
        store.UpsertAsync(Arg.Any<ListingSnapshot>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var snap = ci.Arg<ListingSnapshot>();
                states.TryGetValue(snap.ListingId, out var current);
                var decision = CatalogProjectionGuard.Apply(current, snap);
                states[snap.ListingId] = decision.State;
                return Task.FromResult(decision.Applied);
            });

        var consumer = new ListingProjectionConsumer(store);
        var now = DateTimeOffset.UtcNow;

        var v1Active = CreateSnapshot(listingId, 1, ListingStatus.Active);
        var v3Sold = CreateSnapshot(listingId, 3, ListingStatus.Sold);
        var v4Reactivated = CreateSnapshot(listingId, 4, ListingStatus.Active);

        await consumer.Consume(CreateContext(new ListingCreatedEvent(listingId, Guid.NewGuid(), now, v1Active)));
        await consumer.Consume(CreateContext(new TradeCompletedEvent(
            CorrelationId: Guid.NewGuid(),
            OperationId: Guid.NewGuid(),
            OccurredAt: now,
            Listing: v3Sold,
            TradeId: Guid.NewGuid(),
            BuyerId: Guid.NewGuid(),
            SellerId: v3Sold.SellerId,
            TemplateId: v3Sold.TemplateId,
            Quantity: 1,
            UnitPrice: 1000,
            GrossGem: 1000,
            FeeGem: 50,
            SellerNetGem: 950,
            CompletedAt: now
        )));
        await consumer.Consume(CreateContext(new ListingCreatedEvent(listingId, Guid.NewGuid(), now, v1Active)));
        await consumer.Consume(CreateContext(new ListingReactivatedEvent(listingId, Guid.NewGuid(), now, v4Reactivated)));

        Assert.Equal(3, states[listingId].Version);
        Assert.Equal(ListingStatus.Sold, states[listingId].Status);
    }

    private static ListingSnapshot CreateSnapshot(Guid listingId, int version, ListingStatus status) =>
        new Listing
        {
            ListingId = listingId,
            SellerId = Guid.NewGuid(),
            ItemId = Guid.NewGuid(),
            TemplateId = Guid.NewGuid(),
            ItemName = "Dragon Blade",
            Category = "Weapon",
            Rarity = "Legendary",
            Quantity = 1,
            UnitPrice = 1000,
            GrossGem = 1000,
            FeeGem = 50,
            SellerNetGem = 950,
            FeeBasisPoints = 500,
            PolicyVersion = 1,
            Status = status,
            Version = version,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }.ToSnapshot();

    private static ConsumeContext<T> CreateContext<T>(T message) where T : class
    {
        var ctx = Substitute.For<ConsumeContext<T>>();
        ctx.Message.Returns(message);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }
}
