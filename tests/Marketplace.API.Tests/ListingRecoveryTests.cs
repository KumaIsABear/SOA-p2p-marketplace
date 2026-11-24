using Marketplace.API.Application.Common;
using Marketplace.API.Application.DTOs;
using Marketplace.API.Application.Interfaces;
using Marketplace.API.Application.Options;
using Marketplace.API.Application.Services;
using Marketplace.API.Domain.Entities;
using Marketplace.API.Domain.Enums;
using Marketplace.API.Tests.Helpers;
using Marketplace.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Marketplace.API.Tests;

public sealed class ListingRecoveryTests
{
    [Fact]
    public async Task MK03_UnknownLockReply_Returns202PendingLock_ReplaysSameId_ThenRecoversToActiveWhenLocked()
    {
        await using var dbFixture = new SqliteTestDatabase();
        await dbFixture.InitializeAsync();

        var sellerId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var bus = new RecordingMessageBus();

        var auth = Substitute.For<IAuthStatusClient>();
        auth.GetAccessStatusAsync(sellerId, Arg.Any<CancellationToken>())
            .Returns(new AuthAccessStatusDto(sellerId, "Player", "Active", true, 1));

        var policy = Substitute.For<IPricePolicyProvider>();
        policy.GetRequiredPolicyAsync(templateId, Arg.Any<CancellationToken>())
            .Returns(new PricePolicySnapshot(templateId, 1000, 1000, 2000, 800, 1200, 1, DateTimeOffset.UtcNow));

        var inventory = Substitute.For<IInventoryClient>();
        inventory.GetItemAsync(itemId, Arg.Any<CancellationToken>())
            .Returns(new InventoryItemDto(itemId, sellerId, templateId, "Dragon Blade", "Weapon", "Legendary", true, 1, "Available", null, null));
        inventory.LockItemAsync(Arg.Any<LockItemRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new LockCallResult(LockCallOutcomeType.UnknownFailure));

        var wallet = Substitute.For<IWalletClient>();

        Guid createdListingId;
        await using (var ctx = dbFixture.CreateContext())
        {
            var service = new ListingCommandService(
                ctx,
                auth,
                inventory,
                policy,
                new InMemoryDistributedListingLock(),
                bus.PublishEndpoint,
                bus.SendEndpointProvider,
                Options.Create(new EconomyOptions { FeeBasisPoints = 500, ListingLockTtlSeconds = 20 }),
                NullLogger<ListingCommandService>.Instance
            );

            var first = await service.CreateListingAsync(
                sellerId,
                "mk03-key-001",
                new CreateListingRequest(itemId, 1, 1000),
                CancellationToken.None
            );

            Assert.Equal(202, first.StatusCode);
            Assert.Equal(ListingStatus.PendingLock, first.Listing.Status);
            createdListingId = first.Listing.ListingId;

            var replay = await service.CreateListingAsync(
                sellerId,
                "mk03-key-001",
                new CreateListingRequest(itemId, 1, 1000),
                CancellationToken.None
            );

            Assert.Equal(202, replay.StatusCode);
            Assert.Equal(createdListingId, replay.Listing.ListingId);
        }

        await inventory.DidNotReceive().TryUnlockItemAsync(Arg.Any<Guid>(), Arg.Any<UnlockItemRequestDto>(), Arg.Any<CancellationToken>());

        await using (var ctx = dbFixture.CreateContext())
        {
            var listing = await ctx.Listings.SingleAsync(x => x.ListingId == createdListingId);
            listing.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
            await ctx.SaveChangesAsync();
        }

        inventory.GetLockStatusAsync(createdListingId, Arg.Any<CancellationToken>())
            .Returns(new ItemLockStatusDto(createdListingId, itemId, "Locked"));

        await using (var ctx = dbFixture.CreateContext())
        {
            var recovery = new ListingRecoveryService(
                ctx,
                inventory,
                wallet,
                bus.PublishEndpoint,
                bus.SendEndpointProvider,
                Options.Create(new RecoveryOptions { PendingLockGraceSeconds = 30 }),
                NullLogger<ListingRecoveryService>.Instance
            );

            var recoveredCount = await recovery.RecoverPendingListingsAsync(CancellationToken.None);
            Assert.Equal(1, recoveredCount);
        }

        await using (var ctx = dbFixture.CreateContext())
        {
            var listing = await ctx.Listings.AsNoTracking().SingleAsync(x => x.ListingId == createdListingId);
            Assert.Equal(ListingStatus.Active, listing.Status);
            Assert.Equal(2, listing.Version);
            Assert.Single(bus.Published.OfType<ListingCreatedEvent>());
        }
    }

    [Fact]
    public async Task MK03_PendingLockNotFound_TombstonesBeforeMarkingCreateFailed()
    {
        await using var dbFixture = new SqliteTestDatabase();
        await dbFixture.InitializeAsync();

        var listingId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var bus = new RecordingMessageBus();

        await using (var ctx = dbFixture.CreateContext())
        {
            var old = DateTimeOffset.UtcNow.AddMinutes(-2);
            ctx.Listings.Add(new Listing
            {
                ListingId = listingId,
                SellerId = sellerId,
                ItemId = itemId,
                TemplateId = Guid.NewGuid(),
                ItemName = "Iron Shield",
                Category = "Armor",
                Rarity = "Common",
                Quantity = 1,
                UnitPrice = 500,
                GrossGem = 500,
                FeeGem = 25,
                SellerNetGem = 475,
                FeeBasisPoints = 500,
                PolicyVersion = 1,
                Status = ListingStatus.PendingLock,
                Version = 1,
                CreatedAt = old,
                UpdatedAt = old
            });
            await ctx.SaveChangesAsync();
        }

        var inventory = Substitute.For<IInventoryClient>();
        inventory.GetLockStatusAsync(listingId, Arg.Any<CancellationToken>())
            .Returns(new ItemLockStatusDto(listingId, null, "NotFound"));
        inventory.TryUnlockItemAsync(listingId, Arg.Any<UnlockItemRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await using (var ctx = dbFixture.CreateContext())
        {
            var recovery = new ListingRecoveryService(
                ctx,
                inventory,
                Substitute.For<IWalletClient>(),
                bus.PublishEndpoint,
                bus.SendEndpointProvider,
                Options.Create(new RecoveryOptions { PendingLockGraceSeconds = 30 }),
                NullLogger<ListingRecoveryService>.Instance
            );

            var recoveredCount = await recovery.RecoverPendingListingsAsync(CancellationToken.None);
            Assert.Equal(1, recoveredCount);
        }

        await inventory.Received(1).TryUnlockItemAsync(
            listingId,
            Arg.Is<UnlockItemRequestDto>(r => r.ItemId == itemId && r.SellerId == sellerId),
            Arg.Any<CancellationToken>()
        );

        await using (var ctx = dbFixture.CreateContext())
        {
            var listing = await ctx.Listings.AsNoTracking().SingleAsync(x => x.ListingId == listingId);
            Assert.Equal(ListingStatus.CreateFailed, listing.Status);
            Assert.Empty(bus.Published);
        }
    }

    [Fact]
    public async Task StalledSettlingSaga_WhenWalletAlreadySettled_RecoversForwardOnly()
    {
        await using var dbFixture = new SqliteTestDatabase();
        await dbFixture.InitializeAsync();

        var tradeId = Guid.NewGuid();
        var listingId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var bus = new RecordingMessageBus();
        var old = DateTimeOffset.UtcNow.AddMinutes(-2);

        await using (var ctx = dbFixture.CreateContext())
        {
            ctx.Listings.Add(new Listing
            {
                ListingId = listingId,
                SellerId = sellerId,
                ItemId = itemId,
                TemplateId = templateId,
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
                Status = ListingStatus.Processing,
                Version = 2,
                CreatedAt = old,
                UpdatedAt = old
            });

            ctx.TradeHistories.Add(new TradeHistory
            {
                TradeId = tradeId,
                ListingId = listingId,
                BuyerId = buyerId,
                SellerId = sellerId,
                ItemId = itemId,
                TemplateId = templateId,
                Quantity = 1,
                UnitPrice = 1000,
                GrossGem = 1000,
                FeeGem = 50,
                SellerNetGem = 950,
                Status = TradeStatus.Settling,
                RequiresReview = false,
                IdempotencyKey = "saga-forward-001",
                CreatedAt = old,
                UpdatedAt = old
            });

            ctx.SagaStates.Add(new TradeSagaState
            {
                CorrelationId = tradeId,
                TradeId = tradeId,
                ListingId = listingId,
                BuyerId = buyerId,
                SellerId = sellerId,
                ItemId = itemId,
                TemplateId = templateId,
                Quantity = 1,
                UnitPrice = 1000,
                GrossGem = 1000,
                FeeGem = 50,
                SellerNetGem = 950,
                CurrentState = nameof(TradeStatus.Settling),
                CurrentOperationId = Guid.NewGuid(),
                RetryCount = 2,
                RequiresReview = false,
                UpdatedAt = old
            });

            await ctx.SaveChangesAsync();
        }

        var wallet = Substitute.For<IWalletClient>();
        wallet.GetTradeTraceAsync(tradeId, Arg.Any<CancellationToken>())
            .Returns(new WalletTradeTraceDto(
                tradeId,
                new FundHoldDto(tradeId, buyerId, 1000, "Settled", old),
                "Settled",
                []
            ));

        await using (var ctx = dbFixture.CreateContext())
        {
            var recovery = new ListingRecoveryService(
                ctx,
                Substitute.For<IInventoryClient>(),
                wallet,
                bus.PublishEndpoint,
                bus.SendEndpointProvider,
                Options.Create(new RecoveryOptions { SagaStaleSeconds = 30, MaxSagaRetries = 5 }),
                NullLogger<ListingRecoveryService>.Instance
            );

            var count = await recovery.RecoverStalledSagasAsync(CancellationToken.None);
            Assert.Equal(1, count);
        }

        await using (var ctx = dbFixture.CreateContext())
        {
            var saga = await ctx.SagaStates.AsNoTracking().SingleAsync(x => x.TradeId == tradeId);
            var trade = await ctx.TradeHistories.AsNoTracking().SingleAsync(x => x.TradeId == tradeId);

            Assert.Equal(nameof(TradeStatus.Finalizing), saga.CurrentState);
            Assert.Equal(TradeStatus.Finalizing, trade.Status);
            Assert.Single(bus.Sent, x => x.Message is FinalizeItemTransferCommand);
            Assert.DoesNotContain(bus.Sent, x => x.Message is RefundBuyerFundsCommand or ReverseItemTransferCommand);
        }
    }

    [Fact]
    public async Task StalledSaga_ExhaustedRetries_FlagsRequiresReviewWithoutBlindRefund()
    {
        await using var dbFixture = new SqliteTestDatabase();
        await dbFixture.InitializeAsync();

        var tradeId = Guid.NewGuid();
        var listingId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var bus = new RecordingMessageBus();
        var old = DateTimeOffset.UtcNow.AddMinutes(-2);

        await using (var ctx = dbFixture.CreateContext())
        {
            ctx.Listings.Add(new Listing
            {
                ListingId = listingId,
                SellerId = sellerId,
                ItemId = itemId,
                TemplateId = templateId,
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
                Status = ListingStatus.Processing,
                Version = 2,
                CreatedAt = old,
                UpdatedAt = old
            });

            ctx.TradeHistories.Add(new TradeHistory
            {
                TradeId = tradeId,
                ListingId = listingId,
                BuyerId = buyerId,
                SellerId = sellerId,
                ItemId = itemId,
                TemplateId = templateId,
                Quantity = 1,
                UnitPrice = 1000,
                GrossGem = 1000,
                FeeGem = 50,
                SellerNetGem = 950,
                Status = TradeStatus.Settling,
                RequiresReview = false,
                IdempotencyKey = "saga-exhausted-001",
                CreatedAt = old,
                UpdatedAt = old
            });

            ctx.SagaStates.Add(new TradeSagaState
            {
                CorrelationId = tradeId,
                TradeId = tradeId,
                ListingId = listingId,
                BuyerId = buyerId,
                SellerId = sellerId,
                ItemId = itemId,
                TemplateId = templateId,
                Quantity = 1,
                UnitPrice = 1000,
                GrossGem = 1000,
                FeeGem = 50,
                SellerNetGem = 950,
                CurrentState = nameof(TradeStatus.Settling),
                CurrentOperationId = Guid.NewGuid(),
                RetryCount = 5,
                RequiresReview = false,
                UpdatedAt = old
            });

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

            var count = await recovery.RecoverStalledSagasAsync(CancellationToken.None);
            Assert.Equal(1, count);
        }

        await using (var ctx = dbFixture.CreateContext())
        {
            var saga = await ctx.SagaStates.AsNoTracking().SingleAsync(x => x.TradeId == tradeId);
            var trade = await ctx.TradeHistories.AsNoTracking().SingleAsync(x => x.TradeId == tradeId);
            var listing = await ctx.Listings.AsNoTracking().SingleAsync(x => x.ListingId == listingId);

            Assert.True(saga.RequiresReview);
            Assert.True(trade.RequiresReview);
            Assert.Equal(nameof(TradeStatus.Settling), saga.CurrentState);
            Assert.Equal(ListingStatus.Processing, listing.Status);
            Assert.Empty(bus.Sent);
            Assert.Empty(bus.Published);
        }
    }
}
