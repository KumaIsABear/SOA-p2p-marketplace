using Marketplace.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Wallet.API.Domain.Entities;
using Wallet.API.Domain.Enums;
using Wallet.API.Domain.Errors;
using Wallet.API.Tests.Helpers;
using WalletEntity = Wallet.API.Domain.Entities.Wallet;

namespace Wallet.API.Tests;

public sealed class WalletSagaSettlementTests {
    [Fact]
    public async Task Ut04_SettleTrade_BurnsFeePreservesOtherHoldAndUpdatesLedger() {
        await using var fixture = new WalletTestFixture();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var otherTradeId = Guid.NewGuid();
        var tradeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var seedDb = fixture.CreateDbContext()) {
            seedDb.Wallets.AddRange(
                new WalletEntity {
                    UserId = buyerId,
                    TotalBalance = 1500L,
                    HoldBalance = 1200L,
                    Version = 2L
                },
                new WalletEntity {
                    UserId = sellerId,
                    TotalBalance = 350L,
                    HoldBalance = 0L,
                    Version = 1L
                }
            );
            seedDb.FundHolds.AddRange(
                new FundHold {
                    TradeId = otherTradeId,
                    BuyerId = buyerId,
                    Amount = 200L,
                    Status = FundHoldStatus.Held,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new FundHold {
                    TradeId = tradeId,
                    BuyerId = buyerId,
                    Amount = 1000L,
                    Status = FundHoldStatus.Held,
                    CreatedAt = now,
                    UpdatedAt = now
                }
            );
            await seedDb.SaveChangesAsync();
        }

        var supplyBefore = 1500L + 350L;

        await using (var db = fixture.CreateDbContext()) {
            var service = fixture.CreateSagaService(db);
            var result = await service.SettleTradeAsync(
                new SettleTradeCommand(Guid.NewGuid(), Guid.NewGuid(), tradeId, buyerId, sellerId, 1000L, 50L, 950L),
                CancellationToken.None
            );

            Assert.True(result.IsSettled);
            Assert.Equal(1000L, result.GrossGem);
            Assert.Equal(50L, result.FeeGem);
            Assert.Equal(950L, result.SellerNetGem);
            Assert.Null(result.FailureCode);
        }

        await using (var verifyDb = fixture.CreateDbContext()) {
            var buyer = await verifyDb.Wallets.AsNoTracking().SingleAsync(w => w.UserId == buyerId);
            var seller = await verifyDb.Wallets.AsNoTracking().SingleAsync(w => w.UserId == sellerId);

            Assert.Equal(500L, buyer.TotalBalance);
            Assert.Equal(200L, buyer.HoldBalance);
            Assert.Equal(300L, buyer.AvailableBalance);

            Assert.Equal(1300L, seller.TotalBalance);
            Assert.Equal(0L, seller.HoldBalance);
            Assert.Equal(1300L, seller.AvailableBalance);

            var supplyAfter = buyer.TotalBalance + seller.TotalBalance;
            Assert.Equal(50L, supplyBefore - supplyAfter);

            var settledHold = await verifyDb.FundHolds.AsNoTracking().SingleAsync(h => h.TradeId == tradeId);
            Assert.Equal(FundHoldStatus.Settled, settledHold.Status);

            var otherHold = await verifyDb.FundHolds.AsNoTracking().SingleAsync(h => h.TradeId == otherTradeId);
            Assert.Equal(FundHoldStatus.Held, otherHold.Status);

            var entries = await verifyDb.WalletTransactions.AsNoTracking()
                .Where(t => t.TradeId == tradeId)
                .ToListAsync();
            Assert.Equal(3, entries.Count);

            var debit = Assert.Single(entries, e => e.Type == WalletTransactionType.SettlementDebit);
            Assert.Equal(buyerId, debit.UserId);
            Assert.Equal(-1000L, debit.TotalDelta);
            Assert.Equal(-1000L, debit.HoldDelta);
            Assert.Null(debit.FeeGem);

            var credit = Assert.Single(entries, e => e.Type == WalletTransactionType.SettlementCredit);
            Assert.Equal(sellerId, credit.UserId);
            Assert.Equal(950L, credit.TotalDelta);
            Assert.Equal(0L, credit.HoldDelta);
            Assert.Null(credit.FeeGem);

            var taxBurn = Assert.Single(entries, e => e.Type == WalletTransactionType.TaxBurn);
            Assert.Equal(sellerId, taxBurn.UserId);
            Assert.Equal(0L, taxBurn.TotalDelta);
            Assert.Equal(0L, taxBurn.HoldDelta);
            Assert.Equal(50L, taxBurn.FeeGem);
        }
    }

    [Fact]
    public async Task SettleTrade_MismatchedFeeAndNet_RejectsWithoutMutation() {
        await using var fixture = new WalletTestFixture();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var tradeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var seedDb = fixture.CreateDbContext()) {
            seedDb.Wallets.Add(new WalletEntity {
                UserId = buyerId,
                TotalBalance = 1000L,
                HoldBalance = 1000L,
                Version = 1L
            });
            seedDb.FundHolds.Add(new FundHold {
                TradeId = tradeId,
                BuyerId = buyerId,
                Amount = 1000L,
                Status = FundHoldStatus.Held,
                CreatedAt = now,
                UpdatedAt = now
            });
            await seedDb.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext()) {
            var service = fixture.CreateSagaService(db);
            var result = await service.SettleTradeAsync(
                new SettleTradeCommand(Guid.NewGuid(), Guid.NewGuid(), tradeId, buyerId, sellerId, 1000L, 50L, 960L),
                CancellationToken.None
            );

            Assert.False(result.IsSettled);
            Assert.Equal(WalletErrorCodes.InvalidSettlementAmounts, result.FailureCode);
        }

        await using (var verifyDb = fixture.CreateDbContext()) {
            var buyer = await verifyDb.Wallets.AsNoTracking().SingleAsync(w => w.UserId == buyerId);
            Assert.Equal(1000L, buyer.TotalBalance);
            Assert.Equal(1000L, buyer.HoldBalance);
        }
    }

    [Fact]
    public async Task SettleTrade_ReleasedHold_RejectsWithHoldAlreadyReleased() {
        await using var fixture = new WalletTestFixture();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var tradeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var seedDb = fixture.CreateDbContext()) {
            seedDb.Wallets.Add(new WalletEntity {
                UserId = buyerId,
                TotalBalance = 1000L,
                HoldBalance = 0L,
                Version = 2L
            });
            seedDb.FundHolds.Add(new FundHold {
                TradeId = tradeId,
                BuyerId = buyerId,
                Amount = 1000L,
                Status = FundHoldStatus.Released,
                CreatedAt = now,
                UpdatedAt = now
            });
            await seedDb.SaveChangesAsync();
        }

        await using var db = fixture.CreateDbContext();
        var service = fixture.CreateSagaService(db);
        var result = await service.SettleTradeAsync(
            new SettleTradeCommand(Guid.NewGuid(), Guid.NewGuid(), tradeId, buyerId, sellerId, 1000L, 50L, 950L),
            CancellationToken.None
        );

        Assert.False(result.IsSettled);
        Assert.Equal(WalletErrorCodes.HoldAlreadyReleased, result.FailureCode);
    }
}
