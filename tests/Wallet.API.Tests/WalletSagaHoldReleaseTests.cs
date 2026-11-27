using Marketplace.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Wallet.API.Application.Exceptions;
using Wallet.API.Domain.Entities;
using Wallet.API.Domain.Enums;
using Wallet.API.Domain.Errors;
using Wallet.API.Tests.Helpers;
using WalletEntity = Wallet.API.Domain.Entities.Wallet;

namespace Wallet.API.Tests;

public sealed class WalletSagaHoldReleaseTests {
    [Fact]
    public async Task Ut03_HoldAndRelease_UpdatesBalancesAndRejectsInsufficientFunds() {
        await using var fixture = new WalletTestFixture();
        var buyerId = Guid.NewGuid();
        var existingOtherTradeId = Guid.NewGuid();
        var targetTradeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var seedDb = fixture.CreateDbContext()) {
            seedDb.Wallets.Add(new WalletEntity {
                UserId = buyerId,
                TotalBalance = 1500L,
                HoldBalance = 200L,
                Version = 1L
            });
            seedDb.FundHolds.Add(new FundHold {
                TradeId = existingOtherTradeId,
                BuyerId = buyerId,
                Amount = 200L,
                Status = FundHoldStatus.Held,
                CreatedAt = now,
                UpdatedAt = now
            });
            await seedDb.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext()) {
            var service = fixture.CreateSagaService(db);
            var insufficient = await service.HoldBuyerFundsAsync(
                new HoldBuyerFundsCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), buyerId, 1301L),
                CancellationToken.None
            );

            Assert.False(insufficient.IsHeld);
            Assert.Equal(WalletErrorCodes.InsufficientFunds, insufficient.FailureCode);
        }

        await using (var verifyDb = fixture.CreateDbContext()) {
            var wallet = await verifyDb.Wallets.AsNoTracking().SingleAsync(w => w.UserId == buyerId);
            Assert.Equal(1500L, wallet.TotalBalance);
            Assert.Equal(200L, wallet.HoldBalance);
            Assert.Equal(1300L, wallet.AvailableBalance);
        }

        await using (var db = fixture.CreateDbContext()) {
            var service = fixture.CreateSagaService(db);
            var held = await service.HoldBuyerFundsAsync(
                new HoldBuyerFundsCommand(Guid.NewGuid(), Guid.NewGuid(), targetTradeId, buyerId, 1000L),
                CancellationToken.None
            );

            Assert.True(held.IsHeld);
            Assert.Null(held.FailureCode);
        }

        await using (var verifyDb = fixture.CreateDbContext()) {
            var wallet = await verifyDb.Wallets.AsNoTracking().SingleAsync(w => w.UserId == buyerId);
            Assert.Equal(1500L, wallet.TotalBalance);
            Assert.Equal(1200L, wallet.HoldBalance);
            Assert.Equal(300L, wallet.AvailableBalance);

            var hold = await verifyDb.FundHolds.AsNoTracking().SingleAsync(h => h.TradeId == targetTradeId);
            Assert.Equal(FundHoldStatus.Held, hold.Status);
            Assert.Equal(1000L, hold.Amount);

            var holdTx = await verifyDb.WalletTransactions.AsNoTracking()
                .SingleAsync(t => t.OperationKey == $"hold:{targetTradeId:D}");
            Assert.Equal(WalletTransactionType.Hold, holdTx.Type);
            Assert.Equal(0L, holdTx.TotalDelta);
            Assert.Equal(1000L, holdTx.HoldDelta);
        }

        await using (var db = fixture.CreateDbContext()) {
            var service = fixture.CreateSagaService(db);
            var refunded = await service.RefundBuyerFundsAsync(
                new RefundBuyerFundsCommand(Guid.NewGuid(), Guid.NewGuid(), targetTradeId, buyerId, 1000L),
                CancellationToken.None
            );

            Assert.Equal(targetTradeId, refunded.TradeId);
            Assert.Equal(1000L, refunded.Amount);
        }

        await using (var verifyDb = fixture.CreateDbContext()) {
            var wallet = await verifyDb.Wallets.AsNoTracking().SingleAsync(w => w.UserId == buyerId);
            Assert.Equal(1500L, wallet.TotalBalance);
            Assert.Equal(200L, wallet.HoldBalance);
            Assert.Equal(1300L, wallet.AvailableBalance);

            var releasedHold = await verifyDb.FundHolds.AsNoTracking().SingleAsync(h => h.TradeId == targetTradeId);
            Assert.Equal(FundHoldStatus.Released, releasedHold.Status);

            var otherHold = await verifyDb.FundHolds.AsNoTracking().SingleAsync(h => h.TradeId == existingOtherTradeId);
            Assert.Equal(FundHoldStatus.Held, otherHold.Status);

            var releaseTx = await verifyDb.WalletTransactions.AsNoTracking()
                .SingleAsync(t => t.OperationKey == $"release:{targetTradeId:D}");
            Assert.Equal(WalletTransactionType.Release, releaseTx.Type);
            Assert.Equal(0L, releaseTx.TotalDelta);
            Assert.Equal(-1000L, releaseTx.HoldDelta);
        }
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-100L)]
    public async Task HoldBuyerFunds_NonPositiveAmount_RejectsWithInvalidAmount(long invalidAmount) {
        await using var fixture = new WalletTestFixture();
        var buyerId = Guid.NewGuid();

        await using (var seedDb = fixture.CreateDbContext()) {
            seedDb.Wallets.Add(new WalletEntity {
                UserId = buyerId,
                TotalBalance = 1000L,
                HoldBalance = 0L,
                Version = 1L
            });
            await seedDb.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext()) {
            var service = fixture.CreateSagaService(db);
            var result = await service.HoldBuyerFundsAsync(
                new HoldBuyerFundsCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), buyerId, invalidAmount),
                CancellationToken.None
            );

            Assert.False(result.IsHeld);
            Assert.Equal(WalletErrorCodes.InvalidAmount, result.FailureCode);
        }
    }

    [Fact]
    public async Task RefundBuyerFunds_SettledHold_ThrowsInvariantViolation() {
        await using var fixture = new WalletTestFixture();
        var buyerId = Guid.NewGuid();
        var tradeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var seedDb = fixture.CreateDbContext()) {
            seedDb.Wallets.Add(new WalletEntity {
                UserId = buyerId,
                TotalBalance = 500L,
                HoldBalance = 0L,
                Version = 2L
            });
            seedDb.FundHolds.Add(new FundHold {
                TradeId = tradeId,
                BuyerId = buyerId,
                Amount = 1000L,
                Status = FundHoldStatus.Settled,
                CreatedAt = now,
                UpdatedAt = now
            });
            await seedDb.SaveChangesAsync();
        }

        await using var db = fixture.CreateDbContext();
        var service = fixture.CreateSagaService(db);

        await Assert.ThrowsAsync<WalletInvariantViolationException>(() =>
            service.RefundBuyerFundsAsync(
                new RefundBuyerFundsCommand(Guid.NewGuid(), Guid.NewGuid(), tradeId, buyerId, 1000L),
                CancellationToken.None
            )
        );
    }
}
