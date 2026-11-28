using Marketplace.API.Application.Interfaces;
using Marketplace.API.Application.Options;
using Marketplace.API.Application.Services;
using Marketplace.API.Domain.Entities;
using Marketplace.API.Domain.Enums;
using Marketplace.API.Infrastructure.Messaging;
using Marketplace.API.Infrastructure.Persistence;
using Marketplace.API.Tests.Helpers;
using Marketplace.Contracts.V1;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Marketplace.API.Tests;

public sealed class TradeSagaFaultTests
{
    [Fact]
    public async Task SG04_SettlementRejected_ReversesItemBeforeRefundingFundsAndReactivatesListing()
    {
        await using var dbFixture = new SqliteTestDatabase();
        await dbFixture.InitializeAsync();

        var seed = await SeedSagaAsync(dbFixture, TradeStatus.Settling);
        await using var provider = BuildSagaServiceProvider(dbFixture);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var sagaHarness = harness.GetSagaStateMachineHarness<TradeSagaStateMachine, TradeSagaState>();
        var now = DateTimeOffset.UtcNow;

        await harness.Bus.Publish(new TradeSettlementRejectedEvent(
            seed.TradeId, Guid.NewGuid(), now, seed.TradeId, "SETTLEMENT_REJECTED"));

        Assert.True(await sagaHarness.Consumed.Any<TradeSettlementRejectedEvent>(
            x => x.Context.Message.TradeId == seed.TradeId));
        Assert.True(await harness.Sent.Any<ReverseItemTransferCommand>(
            x => x.Context.Message.TradeId == seed.TradeId));
        await WaitForSagaStateAsync(dbFixture, seed.TradeId, nameof(TradeStatus.ReversingItem));

        await using (var ctx = dbFixture.CreateContext())
        {
            var saga = await ctx.SagaStates.AsNoTracking().SingleAsync(x => x.TradeId == seed.TradeId);
            var listing = await ctx.Listings.AsNoTracking().SingleAsync(x => x.ListingId == seed.ListingId);
            var midSteps = await ctx.SagaSteps.AsNoTracking().Where(x => x.TradeId == seed.TradeId).ToListAsync();

            Assert.Equal(nameof(TradeStatus.ReversingItem), saga.CurrentState);
            Assert.Equal(ListingStatus.Processing, listing.Status);
            Assert.DoesNotContain(midSteps, s => s.Step == "RefundBuyerFunds");
        }

        await harness.Bus.Publish(new ItemTransferReversedEvent(
            seed.TradeId, Guid.NewGuid(), now.AddSeconds(1), seed.TradeId, seed.ListingId, seed.ItemId));

        Assert.True(await sagaHarness.Consumed.Any<ItemTransferReversedEvent>(
            x => x.Context.Message.TradeId == seed.TradeId));
        Assert.True(await harness.Sent.Any<RefundBuyerFundsCommand>(
            x => x.Context.Message.TradeId == seed.TradeId && x.Context.Message.Amount == 1000));
        await WaitForSagaStateAsync(dbFixture, seed.TradeId, nameof(TradeStatus.ReleasingFunds));

        await using (var ctx = dbFixture.CreateContext())
        {
            var saga = await ctx.SagaStates.AsNoTracking().SingleAsync(x => x.TradeId == seed.TradeId);
            Assert.Equal(nameof(TradeStatus.ReleasingFunds), saga.CurrentState);
        }

        await harness.Bus.Publish(new BuyerFundsRefundedEvent(
            seed.TradeId, Guid.NewGuid(), now.AddSeconds(2), seed.TradeId, 1000));

        Assert.True(await sagaHarness.Consumed.Any<BuyerFundsRefundedEvent>(
            x => x.Context.Message.TradeId == seed.TradeId));
        Assert.True(await harness.Published.Any<ListingReactivatedEvent>(
            x => x.Context.Message.Listing.ListingId == seed.ListingId));
        Assert.True(await harness.Published.Any<TradeFailedEvent>(
            x => x.Context.Message.TradeId == seed.TradeId && x.Context.Message.FailureCode == "SETTLEMENT_REJECTED"));
        await WaitForSagaStateAsync(dbFixture, seed.TradeId, nameof(TradeStatus.Failed));

        await using (var ctx = dbFixture.CreateContext())
        {
            var saga = await ctx.SagaStates.AsNoTracking().SingleAsync(x => x.TradeId == seed.TradeId);
            var trade = await ctx.TradeHistories.AsNoTracking().SingleAsync(x => x.TradeId == seed.TradeId);
            var listing = await ctx.Listings.AsNoTracking().SingleAsync(x => x.ListingId == seed.ListingId);
            var steps = await ctx.SagaSteps.AsNoTracking()
                .Where(x => x.TradeId == seed.TradeId)
                .OrderBy(x => x.At)
                .ToListAsync();

            Assert.Equal(nameof(TradeStatus.Failed), saga.CurrentState);
            Assert.Equal(TradeStatus.Failed, trade.Status);
            Assert.Equal("SETTLEMENT_REJECTED", trade.FailureCode);
            Assert.Equal(ListingStatus.Active, listing.Status);
            Assert.Contains(steps, s => s.Step == "SettleTrade" && s.Outcome == "Rejected");
            Assert.Contains(steps, s => s.Step == "ReverseItemTransfer" && s.Outcome == "Succeeded");
            Assert.Contains(steps, s => s.Step == "RefundBuyerFunds" && s.Outcome == "Succeeded");
        }
    }

    [Fact]
    public async Task SG06_TransientFinalizeFailure_RetriesWithoutRefundThenCompletesOnFinalized()
    {
        await using var dbFixture = new SqliteTestDatabase();
        await dbFixture.InitializeAsync();

        var seed = await SeedSagaAsync(dbFixture, TradeStatus.Finalizing, updatedAt: DateTimeOffset.UtcNow.AddMinutes(-2));
        var bus = new RecordingMessageBus();

        await using (var ctx = dbFixture.CreateContext())
        {
            var recovery = new ListingRecoveryService(
                ctx,
                Substitute.For<IInventoryClient>(),
                Substitute.For<IWalletClient>(),
                bus.PublishEndpoint,
                bus.SendEndpointProvider,
                Options.Create(new RecoveryOptions { SagaStaleSeconds = 30, MaxSagaRetries = 5 }),
                NullLogger<ListingRecoveryService>.Instance
            );

            var processed = await recovery.RecoverStalledSagasAsync(CancellationToken.None);
            Assert.Equal(1, processed);
        }

        var retriedCmd = Assert.Single(bus.Sent, x => x.Message is FinalizeItemTransferCommand);
        Assert.Equal(seed.OperationId, ((FinalizeItemTransferCommand)retriedCmd.Message).OperationId);
        Assert.DoesNotContain(bus.Sent, x => x.Message is RefundBuyerFundsCommand or ReverseItemTransferCommand or SettleTradeCommand);

        await using (var ctx = dbFixture.CreateContext())
        {
            var saga = await ctx.SagaStates.AsNoTracking().SingleAsync(x => x.TradeId == seed.TradeId);
            var listing = await ctx.Listings.AsNoTracking().SingleAsync(x => x.ListingId == seed.ListingId);
            Assert.Equal(nameof(TradeStatus.Finalizing), saga.CurrentState);
            Assert.Equal(1, saga.RetryCount);
            Assert.Equal(ListingStatus.Processing, listing.Status);
        }

        await using var provider = BuildSagaServiceProvider(dbFixture);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var sagaHarness = harness.GetSagaStateMachineHarness<TradeSagaStateMachine, TradeSagaState>();
        await harness.Bus.Publish(new ItemTransferFinalizedEvent(
            seed.TradeId, seed.OperationId, DateTimeOffset.UtcNow, seed.TradeId, seed.ListingId, seed.ItemId));

        Assert.True(await sagaHarness.Consumed.Any<ItemTransferFinalizedEvent>(
            x => x.Context.Message.TradeId == seed.TradeId));
        Assert.True(await harness.Published.Any<TradeCompletedEvent>(
            x => x.Context.Message.TradeId == seed.TradeId));
        await WaitForSagaStateAsync(dbFixture, seed.TradeId, nameof(TradeStatus.Completed));
        Assert.DoesNotContain(harness.Sent.Select<RefundBuyerFundsCommand>(),
            x => x.Context.Message.TradeId == seed.TradeId);

        await using (var ctx = dbFixture.CreateContext())
        {
            var saga = await ctx.SagaStates.AsNoTracking().SingleAsync(x => x.TradeId == seed.TradeId);
            var trade = await ctx.TradeHistories.AsNoTracking().SingleAsync(x => x.TradeId == seed.TradeId);
            var listing = await ctx.Listings.AsNoTracking().SingleAsync(x => x.ListingId == seed.ListingId);

            Assert.Equal(nameof(TradeStatus.Completed), saga.CurrentState);
            Assert.Equal(TradeStatus.Completed, trade.Status);
            Assert.Equal(ListingStatus.Sold, listing.Status);
        }
    }

    [Fact]
    public async Task SG07_DuplicateAndStaleEvents_ProduceSingleBusinessEffectWithNoInvalidAdvance()
    {
        await using var dbFixture = new SqliteTestDatabase();
        await dbFixture.InitializeAsync();

        var seed = await SeedSagaAsync(dbFixture, TradeStatus.Finalizing);
        await using var provider = BuildSagaServiceProvider(dbFixture);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var sagaHarness = harness.GetSagaStateMachineHarness<TradeSagaStateMachine, TradeSagaState>();
        var now = DateTimeOffset.UtcNow;

        var staleHoldOp = Guid.NewGuid();
        var staleSettleOp = Guid.NewGuid();
        await harness.Bus.Publish(new BuyerFundsHeldEvent(seed.TradeId, staleHoldOp, now, seed.TradeId, seed.BuyerId, 1000));
        await harness.Bus.Publish(new TradeSettledEvent(seed.TradeId, staleSettleOp, now, seed.TradeId, 1000, 50, 950));

        Assert.True(await sagaHarness.Consumed.Any<BuyerFundsHeldEvent>(x => x.Context.Message.OperationId == staleHoldOp));
        Assert.True(await sagaHarness.Consumed.Any<TradeSettledEvent>(x => x.Context.Message.OperationId == staleSettleOp));

        await using (var ctx = dbFixture.CreateContext())
        {
            var saga = await ctx.SagaStates.AsNoTracking().SingleAsync(x => x.TradeId == seed.TradeId);
            var steps = await ctx.SagaSteps.AsNoTracking().Where(x => x.TradeId == seed.TradeId).ToListAsync();
            Assert.Equal(nameof(TradeStatus.Finalizing), saga.CurrentState);
            Assert.Empty(steps);
        }

        var firstFinalOp = Guid.NewGuid();
        var dupFinalOp = Guid.NewGuid();
        await harness.Bus.Publish(new ItemTransferFinalizedEvent(seed.TradeId, firstFinalOp, now, seed.TradeId, seed.ListingId, seed.ItemId));
        Assert.True(await sagaHarness.Consumed.Any<ItemTransferFinalizedEvent>(x => x.Context.Message.OperationId == firstFinalOp));
        Assert.True(await harness.Published.Any<TradeCompletedEvent>(x => x.Context.Message.TradeId == seed.TradeId));
        await WaitForSagaStateAsync(dbFixture, seed.TradeId, nameof(TradeStatus.Completed));

        await harness.Bus.Publish(new ItemTransferFinalizedEvent(seed.TradeId, dupFinalOp, now.AddSeconds(1), seed.TradeId, seed.ListingId, seed.ItemId));
        Assert.True(await sagaHarness.Consumed.Any<ItemTransferFinalizedEvent>(x => x.Context.Message.OperationId == dupFinalOp));

        var completedEvents = harness.Published.Select<TradeCompletedEvent>()
            .Count(x => x.Context.Message.TradeId == seed.TradeId);
        Assert.Equal(1, completedEvents);

        Assert.DoesNotContain(harness.Sent.Select<TransferItemCommand>(),
            x => x.Context.Message.TradeId == seed.TradeId);
        Assert.DoesNotContain(harness.Sent.Select<FinalizeItemTransferCommand>(),
            x => x.Context.Message.TradeId == seed.TradeId);

        await using (var ctx = dbFixture.CreateContext())
        {
            var listing = await ctx.Listings.AsNoTracking().SingleAsync(x => x.ListingId == seed.ListingId);
            var steps = await ctx.SagaSteps.AsNoTracking().Where(x => x.TradeId == seed.TradeId).ToListAsync();
            Assert.Equal(ListingStatus.Sold, listing.Status);
            Assert.Equal(3, listing.Version);
            Assert.Single(steps);
        }
    }

    [Fact]
    public async Task SG08_FinalizingExhaustedRetries_FlagsRequiresReviewAndSupportsSafeManualRedrive()
    {
        await using var dbFixture = new SqliteTestDatabase();
        await dbFixture.InitializeAsync();

        var old = DateTimeOffset.UtcNow.AddMinutes(-2);
        var seed = await SeedSagaAsync(dbFixture, TradeStatus.Finalizing, retryCount: 5, updatedAt: old);
        var bus = new RecordingMessageBus();

        await using (var ctx = dbFixture.CreateContext())
        {
            var recovery = new ListingRecoveryService(
                ctx,
                Substitute.For<IInventoryClient>(),
                Substitute.For<IWalletClient>(),
                bus.PublishEndpoint,
                bus.SendEndpointProvider,
                Options.Create(new RecoveryOptions { SagaStaleSeconds = 30, MaxSagaRetries = 5 }),
                NullLogger<ListingRecoveryService>.Instance
            );

            var processed = await recovery.RecoverStalledSagasAsync(CancellationToken.None);
            Assert.Equal(1, processed);
        }

        await using (var ctx = dbFixture.CreateContext())
        {
            var saga = await ctx.SagaStates.AsNoTracking().SingleAsync(x => x.TradeId == seed.TradeId);
            var trade = await ctx.TradeHistories.AsNoTracking().SingleAsync(x => x.TradeId == seed.TradeId);
            var listing = await ctx.Listings.AsNoTracking().SingleAsync(x => x.ListingId == seed.ListingId);

            Assert.True(saga.RequiresReview);
            Assert.True(trade.RequiresReview);
            Assert.Equal(nameof(TradeStatus.Finalizing), saga.CurrentState);
            Assert.Equal(ListingStatus.Processing, listing.Status);
            Assert.Empty(bus.Sent);
        }

        await using (var ctx = dbFixture.CreateContext())
        {
            var saga = await ctx.SagaStates.SingleAsync(x => x.TradeId == seed.TradeId);
            saga.RequiresReview = false;
            saga.RetryCount = 0;
            saga.UpdatedAt = old;
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = dbFixture.CreateContext())
        {
            var recovery = new ListingRecoveryService(
                ctx,
                Substitute.For<IInventoryClient>(),
                Substitute.For<IWalletClient>(),
                bus.PublishEndpoint,
                bus.SendEndpointProvider,
                Options.Create(new RecoveryOptions { SagaStaleSeconds = 30, MaxSagaRetries = 5 }),
                NullLogger<ListingRecoveryService>.Instance
            );

            var processed = await recovery.RecoverStalledSagasAsync(CancellationToken.None);
            Assert.Equal(1, processed);
        }

        var redriven = Assert.Single(bus.Sent, x => x.Message is FinalizeItemTransferCommand);
        Assert.Equal(seed.OperationId, ((FinalizeItemTransferCommand)redriven.Message).OperationId);
    }

    private static async Task WaitForSagaStateAsync(
        SqliteTestDatabase dbFixture,
        Guid tradeId,
        string expectedState)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            await using var ctx = dbFixture.CreateContext();
            var state = await ctx.SagaStates.AsNoTracking()
                .Where(x => x.TradeId == tradeId)
                .Select(x => x.CurrentState)
                .SingleAsync();

            if (state == expectedState)
                return;

            await Task.Delay(25);
        }
    }

    private static ServiceProvider BuildSagaServiceProvider(SqliteTestDatabase dbFixture)
    {
        return new ServiceCollection()
            .AddLogging()
            .AddScoped<MarketplaceDbContext>(_ => dbFixture.CreateContext())
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddSagaStateMachine<TradeSagaStateMachine, TradeSagaState>()
                    .EntityFrameworkRepository(r =>
                    {
                        r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                        r.ExistingDbContext<MarketplaceDbContext>();
                    });
            })
            .BuildServiceProvider(true);
    }

    private static async Task<SagaSeed> SeedSagaAsync(
        SqliteTestDatabase dbFixture,
        TradeStatus state,
        int retryCount = 0,
        DateTimeOffset? updatedAt = null)
    {
        var now = updatedAt ?? DateTimeOffset.UtcNow;
        var seed = new SagaSeed(
            TradeId: Guid.NewGuid(),
            ListingId: Guid.NewGuid(),
            BuyerId: Guid.NewGuid(),
            SellerId: Guid.NewGuid(),
            ItemId: Guid.NewGuid(),
            TemplateId: Guid.NewGuid(),
            OperationId: Guid.NewGuid()
        );

        await using var ctx = dbFixture.CreateContext();
        ctx.Listings.Add(new Listing
        {
            ListingId = seed.ListingId,
            SellerId = seed.SellerId,
            ItemId = seed.ItemId,
            TemplateId = seed.TemplateId,
            ItemName = "Dragon Fang Sword",
            Category = "Weapon",
            Rarity = "Legendary",
            Quantity = 1,
            UnitPrice = 1000,
            GrossGem = 1000,
            FeeGem = 50,
            SellerNetGem = 950,
            FeeBasisPoints = 500,
            PolicyVersion = 1,
            Status = ListingStatus.Processing,
            Version = 2,
            CreatedAt = now,
            UpdatedAt = now
        });

        ctx.TradeHistories.Add(new TradeHistory
        {
            TradeId = seed.TradeId,
            ListingId = seed.ListingId,
            BuyerId = seed.BuyerId,
            SellerId = seed.SellerId,
            ItemId = seed.ItemId,
            TemplateId = seed.TemplateId,
            Quantity = 1,
            UnitPrice = 1000,
            GrossGem = 1000,
            FeeGem = 50,
            SellerNetGem = 950,
            Status = state,
            RequiresReview = false,
            IdempotencyKey = $"sg-fault-{seed.TradeId:N}",
            CreatedAt = now,
            UpdatedAt = now
        });

        ctx.SagaStates.Add(new TradeSagaState
        {
            CorrelationId = seed.TradeId,
            TradeId = seed.TradeId,
            ListingId = seed.ListingId,
            BuyerId = seed.BuyerId,
            SellerId = seed.SellerId,
            ItemId = seed.ItemId,
            TemplateId = seed.TemplateId,
            Quantity = 1,
            UnitPrice = 1000,
            GrossGem = 1000,
            FeeGem = 50,
            SellerNetGem = 950,
            CurrentState = state.ToString(),
            CurrentOperationId = seed.OperationId,
            RetryCount = retryCount,
            RequiresReview = false,
            UpdatedAt = now
        });

        await ctx.SaveChangesAsync();
        return seed;
    }

    private sealed record SagaSeed(
        Guid TradeId,
        Guid ListingId,
        Guid BuyerId,
        Guid SellerId,
        Guid ItemId,
        Guid TemplateId,
        Guid OperationId
    );
}
