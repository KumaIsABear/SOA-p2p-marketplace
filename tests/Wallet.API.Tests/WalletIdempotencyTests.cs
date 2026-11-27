using Marketplace.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Wallet.API.Application.Services;
using Wallet.API.Domain.Entities;
using Wallet.API.Domain.Enums;
using Wallet.API.Tests.Helpers;
using WalletEntity = Wallet.API.Domain.Entities.Wallet;

namespace Wallet.API.Tests;

public sealed class WalletIdempotencyTests {
    [Fact]
    public async Task Ut05_TopUpSettlement_ExactReplayDoesNotDoubleCredit() {
        await using var fixture = new WalletTestFixture();
        var userId = Guid.NewGuid();
        var topUpId = Guid.NewGuid();
        var appTransId = "261127_100001";
        var zpTransId = 9900112233L;
        var now = DateTimeOffset.UtcNow;

        await using (var seedDb = fixture.CreateDbContext()) {
            seedDb.Wallets.Add(new WalletEntity {
                UserId = userId,
                TotalBalance = 100L,
                HoldBalance = 0L,
                Version = 1L
            });
            seedDb.TopUpOrders.Add(new TopUpOrder {
                TopUpId = topUpId,
                UserId = userId,
                Provider = TopUpOrder.DefaultProvider,
                AppTransId = appTransId,
                AmountVnd = 100000L,
                GemAmount = 1000L,
                Status = TopUpOrderStatus.Pending,
                CreatedAt = now
            });
            await seedDb.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext()) {
            var service = fixture.CreateTopUpSettlementService(db);
            var first = await service.SettleTopUpAsync(
                new TopUpSettlementRequest(appTransId, zpTransId, 100000L, userId, "Callback"),
                CancellationToken.None
            );

            Assert.Equal(TopUpSettlementOutcome.Settled, first.Outcome);
            Assert.Equal(topUpId, first.TopUpId);
        }

        await using (var db = fixture.CreateDbContext()) {
            var service = fixture.CreateTopUpSettlementService(db);
            var replay = await service.SettleTopUpAsync(
                new TopUpSettlementRequest(appTransId, zpTransId, 100000L, userId, "ReconciliationWorker"),
                CancellationToken.None
            );

            Assert.Equal(TopUpSettlementOutcome.Duplicate, replay.Outcome);
            Assert.Equal(topUpId, replay.TopUpId);
        }

        await using (var db = fixture.CreateDbContext()) {
            var service = fixture.CreateTopUpSettlementService(db);
            var conflict = await service.SettleTopUpAsync(
                new TopUpSettlementRequest(appTransId, zpTransId + 1L, 100000L, userId, "Callback"),
                CancellationToken.None
            );

            Assert.Equal(TopUpSettlementOutcome.Discrepancy, conflict.Outcome);
        }

        await using (var verifyDb = fixture.CreateDbContext()) {
            var wallet = await verifyDb.Wallets.AsNoTracking().SingleAsync(w => w.UserId == userId);
            Assert.Equal(1100L, wallet.TotalBalance);
            Assert.Equal(0L, wallet.HoldBalance);

            var order = await verifyDb.TopUpOrders.AsNoTracking().SingleAsync(o => o.TopUpId == topUpId);
            Assert.Equal(TopUpOrderStatus.Paid, order.Status);
            Assert.Equal(zpTransId, order.ProviderTxnId);

            var topUpEntries = await verifyDb.WalletTransactions.AsNoTracking()
                .Where(t => t.TopUpId == topUpId)
                .ToListAsync();
            Assert.Single(topUpEntries);
            Assert.Equal($"topup:{topUpId:D}", topUpEntries[0].OperationKey);
            Assert.Equal(1000L, topUpEntries[0].TotalDelta);
        }
    }

    [Fact]
    public async Task Ut05_SagaOperations_ReplayingHoldSettleAndReleaseProduceSingleEffect() {
        await using var fixture = new WalletTestFixture();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var settledTradeId = Guid.NewGuid();
        var releasedTradeId = Guid.NewGuid();

        await using (var seedDb = fixture.CreateDbContext()) {
            seedDb.Wallets.AddRange(
                new WalletEntity {
                    UserId = buyerId,
                    TotalBalance = 2000L,
                    HoldBalance = 0L,
                    Version = 1L
                },
                new WalletEntity {
                    UserId = sellerId,
                    TotalBalance = 100L,
                    HoldBalance = 0L,
                    Version = 1L
                }
            );
            await seedDb.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext()) {
            var service = fixture.CreateSagaService(db);

            var hold1 = await service.HoldBuyerFundsAsync(
                new HoldBuyerFundsCommand(Guid.NewGuid(), Guid.NewGuid(), settledTradeId, buyerId, 1000L),
                CancellationToken.None
            );
            var holdReplay = await service.HoldBuyerFundsAsync(
                new HoldBuyerFundsCommand(Guid.NewGuid(), Guid.NewGuid(), settledTradeId, buyerId, 1000L),
                CancellationToken.None
            );

            Assert.True(hold1.IsHeld);
            Assert.True(holdReplay.IsHeld);

            var settle1 = await service.SettleTradeAsync(
                new SettleTradeCommand(Guid.NewGuid(), Guid.NewGuid(), settledTradeId, buyerId, sellerId, 1000L, 50L, 950L),
                CancellationToken.None
            );
            var settleReplay = await service.SettleTradeAsync(
                new SettleTradeCommand(Guid.NewGuid(), Guid.NewGuid(), settledTradeId, buyerId, sellerId, 1000L, 50L, 950L),
                CancellationToken.None
            );

            Assert.True(settle1.IsSettled);
            Assert.True(settleReplay.IsSettled);

            var hold2 = await service.HoldBuyerFundsAsync(
                new HoldBuyerFundsCommand(Guid.NewGuid(), Guid.NewGuid(), releasedTradeId, buyerId, 500L),
                CancellationToken.None
            );
            Assert.True(hold2.IsHeld);

            var refund1 = await service.RefundBuyerFundsAsync(
                new RefundBuyerFundsCommand(Guid.NewGuid(), Guid.NewGuid(), releasedTradeId, buyerId, 500L),
                CancellationToken.None
            );
            var refundReplay = await service.RefundBuyerFundsAsync(
                new RefundBuyerFundsCommand(Guid.NewGuid(), Guid.NewGuid(), releasedTradeId, buyerId, 500L),
                CancellationToken.None
            );

            Assert.Equal(500L, refund1.Amount);
            Assert.Equal(500L, refundReplay.Amount);
        }

        await using (var verifyDb = fixture.CreateDbContext()) {
            var buyer = await verifyDb.Wallets.AsNoTracking().SingleAsync(w => w.UserId == buyerId);
            var seller = await verifyDb.Wallets.AsNoTracking().SingleAsync(w => w.UserId == sellerId);

            Assert.Equal(1000L, buyer.TotalBalance);
            Assert.Equal(0L, buyer.HoldBalance);
            Assert.Equal(1050L, seller.TotalBalance);

            var settledTradeEntries = await verifyDb.WalletTransactions.AsNoTracking()
                .Where(t => t.TradeId == settledTradeId)
                .ToListAsync();
            Assert.Equal(4, settledTradeEntries.Count);

            var releasedTradeEntries = await verifyDb.WalletTransactions.AsNoTracking()
                .Where(t => t.TradeId == releasedTradeId)
                .ToListAsync();
            Assert.Equal(2, releasedTradeEntries.Count);
        }
    }
}
