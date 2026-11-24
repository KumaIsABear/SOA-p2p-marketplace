using Marketplace.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Wallet.API.Application.Exceptions;
using Wallet.API.Domain.Entities;
using Wallet.API.Domain.Enums;
using Wallet.API.Domain.Errors;
using Wallet.API.Infrastructure.Persistence;

namespace Wallet.API.Application.Services;

public sealed class WalletSagaService : IWalletSagaService
{
    private readonly WalletDbContext _dbContext;

    public WalletSagaService(WalletDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<HoldBuyerFundsResult> HoldBuyerFundsAsync(
        HoldBuyerFundsCommand command,
        CancellationToken cancellationToken)
    {
        return ExecuteInTransactionAsync(
            ct => HoldBuyerFundsCoreAsync(command, ct),
            cancellationToken);
    }

    public Task<SettleTradeResult> SettleTradeAsync(
        SettleTradeCommand command,
        CancellationToken cancellationToken)
    {
        return ExecuteInTransactionAsync(
            ct => SettleTradeCoreAsync(command, ct),
            cancellationToken);
    }

    public Task<RefundBuyerFundsResult> RefundBuyerFundsAsync(
        RefundBuyerFundsCommand command,
        CancellationToken cancellationToken)
    {
        return ExecuteInTransactionAsync(
            ct => RefundBuyerFundsCoreAsync(command, ct),
            cancellationToken);
    }

    private async Task<HoldBuyerFundsResult> HoldBuyerFundsCoreAsync(
        HoldBuyerFundsCommand command,
        CancellationToken cancellationToken)
    {
        var existingRejection = await FindRejectionAsync(command.OperationId, cancellationToken);
        if (existingRejection is not null)
        {
            if (existingRejection.OperationType != WalletOperationType.Hold
                || existingRejection.TradeId != command.TradeId)
            {
                throw new WalletInvariantViolationException(
                    $"Operation '{command.OperationId}' was already rejected for a different operation or trade.");
            }

            return HoldBuyerFundsResult.Rejected(
                command.TradeId,
                command.BuyerId,
                command.Amount,
                existingRejection.FailureCode);
        }

        if (command.Amount <= 0)
        {
            var unexpectedHold = await LockFundHoldAsync(command.TradeId, cancellationToken);
            if (unexpectedHold is not null)
            {
                throw new WalletInvariantViolationException(
                    $"Fund hold for trade '{command.TradeId}' already exists while incoming hold amount is invalid.");
            }

            await PersistRejectionAsync(
                command.OperationId,
                command.TradeId,
                WalletOperationType.Hold,
                WalletErrorCodes.InvalidAmount,
                cancellationToken);

            return HoldBuyerFundsResult.Rejected(
                command.TradeId,
                command.BuyerId,
                command.Amount,
                WalletErrorCodes.InvalidAmount);
        }

        await EnsureWalletExistsAsync(command.BuyerId, cancellationToken);
        var buyerWallet = await LockWalletAsync(command.BuyerId, cancellationToken)
            ?? throw new WalletInvariantViolationException(
                $"Buyer wallet '{command.BuyerId}' could not be loaded after initialization.");

        var existingHold = await LockFundHoldAsync(command.TradeId, cancellationToken);
        if (existingHold is not null)
        {
            if (existingHold.BuyerId != command.BuyerId || existingHold.Amount != command.Amount)
            {
                throw new WalletInvariantViolationException(
                    $"Existing fund hold for trade '{command.TradeId}' does not match buyer or amount.");
            }

            return HoldBuyerFundsResult.Held(existingHold.TradeId, existingHold.BuyerId, existingHold.Amount);
        }

        var availableBalance = checked(buyerWallet.TotalBalance - buyerWallet.HoldBalance);
        if (availableBalance < command.Amount)
        {
            await PersistRejectionAsync(
                command.OperationId,
                command.TradeId,
                WalletOperationType.Hold,
                WalletErrorCodes.InsufficientFunds,
                cancellationToken);

            return HoldBuyerFundsResult.Rejected(
                command.TradeId,
                command.BuyerId,
                command.Amount,
                WalletErrorCodes.InsufficientFunds);
        }

        var now = DateTimeOffset.UtcNow;

        buyerWallet.HoldBalance = checked(buyerWallet.HoldBalance + command.Amount);
        buyerWallet.Version = checked(buyerWallet.Version + 1);

        _dbContext.FundHolds.Add(new FundHold
        {
            TradeId = command.TradeId,
            BuyerId = command.BuyerId,
            Amount = command.Amount,
            Status = FundHoldStatus.Held,
            CreatedAt = now,
            UpdatedAt = now
        });

        _dbContext.WalletTransactions.Add(new WalletTransaction
        {
            EntryId = Guid.NewGuid(),
            UserId = command.BuyerId,
            OperationKey = $"hold:{command.TradeId:D}",
            TradeId = command.TradeId,
            TopUpId = null,
            TotalDelta = 0L,
            HoldDelta = command.Amount,
            FeeGem = null,
            Type = WalletTransactionType.Hold,
            CreatedAt = now
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return HoldBuyerFundsResult.Held(command.TradeId, command.BuyerId, command.Amount);
    }

    private async Task<SettleTradeResult> SettleTradeCoreAsync(
        SettleTradeCommand command,
        CancellationToken cancellationToken)
    {
        var existingRejection = await FindRejectionAsync(command.OperationId, cancellationToken);
        if (existingRejection is not null)
        {
            if (existingRejection.OperationType != WalletOperationType.Settle
                || existingRejection.TradeId != command.TradeId)
            {
                throw new WalletInvariantViolationException(
                    $"Operation '{command.OperationId}' was already rejected for a different operation or trade.");
            }

            return SettleTradeResult.Rejected(command.TradeId, existingRejection.FailureCode);
        }

        if (command.BuyerId == command.SellerId)
        {
            await PersistRejectionAsync(
                command.OperationId,
                command.TradeId,
                WalletOperationType.Settle,
                WalletErrorCodes.SelfPurchase,
                cancellationToken);

            return SettleTradeResult.Rejected(command.TradeId, WalletErrorCodes.SelfPurchase);
        }

        if (!AreSettlementAmountsValid(command.GrossGem, command.FeeGem, command.SellerNetGem))
        {
            await PersistRejectionAsync(
                command.OperationId,
                command.TradeId,
                WalletOperationType.Settle,
                WalletErrorCodes.InvalidSettlementAmounts,
                cancellationToken);

            return SettleTradeResult.Rejected(command.TradeId, WalletErrorCodes.InvalidSettlementAmounts);
        }

        await EnsureWalletExistsAsync(command.SellerId, cancellationToken);

        var (firstUserId, secondUserId) = command.BuyerId.CompareTo(command.SellerId) < 0
            ? (command.BuyerId, command.SellerId)
            : (command.SellerId, command.BuyerId);

        var firstWallet = await LockWalletAsync(firstUserId, cancellationToken);
        var secondWallet = await LockWalletAsync(secondUserId, cancellationToken);

        var buyerWallet = firstUserId == command.BuyerId ? firstWallet : secondWallet;
        var sellerWallet = firstUserId == command.SellerId ? firstWallet : secondWallet;

        var hold = await LockFundHoldAsync(command.TradeId, cancellationToken);
        if (hold is null)
        {
            await PersistRejectionAsync(
                command.OperationId,
                command.TradeId,
                WalletOperationType.Settle,
                WalletErrorCodes.HoldNotFound,
                cancellationToken);

            return SettleTradeResult.Rejected(command.TradeId, WalletErrorCodes.HoldNotFound);
        }

        if (hold.Status == FundHoldStatus.Settled)
        {
            if (hold.BuyerId != command.BuyerId || hold.Amount != command.GrossGem)
            {
                throw new WalletInvariantViolationException(
                    $"Settled fund hold for trade '{command.TradeId}' does not match incoming settlement command.");
            }

            var existingCredit = await _dbContext.WalletTransactions
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    t => t.TradeId == command.TradeId && t.Type == WalletTransactionType.SettlementCredit,
                    cancellationToken);

            if (existingCredit is not null
                && (existingCredit.UserId != command.SellerId || existingCredit.TotalDelta != command.SellerNetGem))
            {
                throw new WalletInvariantViolationException(
                    $"Existing settlement credit for trade '{command.TradeId}' does not match seller or net amount.");
            }

            return SettleTradeResult.Settled(
                command.TradeId,
                command.GrossGem,
                command.FeeGem,
                command.SellerNetGem);
        }

        if (hold.Status == FundHoldStatus.Released)
        {
            await PersistRejectionAsync(
                command.OperationId,
                command.TradeId,
                WalletOperationType.Settle,
                WalletErrorCodes.HoldAlreadyReleased,
                cancellationToken);

            return SettleTradeResult.Rejected(command.TradeId, WalletErrorCodes.HoldAlreadyReleased);
        }

        if (hold.Status != FundHoldStatus.Held)
        {
            await PersistRejectionAsync(
                command.OperationId,
                command.TradeId,
                WalletOperationType.Settle,
                WalletErrorCodes.HoldInvalidStatus,
                cancellationToken);

            return SettleTradeResult.Rejected(command.TradeId, WalletErrorCodes.HoldInvalidStatus);
        }

        if (hold.BuyerId != command.BuyerId || hold.Amount != command.GrossGem)
        {
            await PersistRejectionAsync(
                command.OperationId,
                command.TradeId,
                WalletOperationType.Settle,
                WalletErrorCodes.HoldMismatch,
                cancellationToken);

            return SettleTradeResult.Rejected(command.TradeId, WalletErrorCodes.HoldMismatch);
        }

        if (buyerWallet is null || sellerWallet is null)
        {
            throw new WalletInvariantViolationException(
                $"Buyer or seller wallet could not be locked for trade '{command.TradeId}'.");
        }

        if (buyerWallet.TotalBalance < command.GrossGem || buyerWallet.HoldBalance < command.GrossGem)
        {
            throw new WalletInvariantViolationException(
                $"Buyer wallet '{command.BuyerId}' has insufficient total or held balance to settle trade '{command.TradeId}'.");
        }

        var now = DateTimeOffset.UtcNow;

        buyerWallet.TotalBalance = checked(buyerWallet.TotalBalance - command.GrossGem);
        buyerWallet.HoldBalance = checked(buyerWallet.HoldBalance - command.GrossGem);
        buyerWallet.Version = checked(buyerWallet.Version + 1);

        sellerWallet.TotalBalance = checked(sellerWallet.TotalBalance + command.SellerNetGem);
        sellerWallet.Version = checked(sellerWallet.Version + 1);

        hold.Status = FundHoldStatus.Settled;
        hold.UpdatedAt = now;

        _dbContext.WalletTransactions.AddRange(
            new WalletTransaction
            {
                EntryId = Guid.NewGuid(),
                UserId = command.BuyerId,
                OperationKey = $"settle:{command.TradeId:D}:debit:{command.BuyerId:D}",
                TradeId = command.TradeId,
                TopUpId = null,
                TotalDelta = checked(-command.GrossGem),
                HoldDelta = checked(-command.GrossGem),
                FeeGem = null,
                Type = WalletTransactionType.SettlementDebit,
                CreatedAt = now
            },
            new WalletTransaction
            {
                EntryId = Guid.NewGuid(),
                UserId = command.SellerId,
                OperationKey = $"settle:{command.TradeId:D}:credit:{command.SellerId:D}",
                TradeId = command.TradeId,
                TopUpId = null,
                TotalDelta = command.SellerNetGem,
                HoldDelta = 0L,
                FeeGem = null,
                Type = WalletTransactionType.SettlementCredit,
                CreatedAt = now
            },
            new WalletTransaction
            {
                EntryId = Guid.NewGuid(),
                UserId = command.SellerId,
                OperationKey = $"settle:{command.TradeId:D}:taxburn:{command.SellerId:D}",
                TradeId = command.TradeId,
                TopUpId = null,
                TotalDelta = 0L,
                HoldDelta = 0L,
                FeeGem = command.FeeGem,
                Type = WalletTransactionType.TaxBurn,
                CreatedAt = now
            });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return SettleTradeResult.Settled(
            command.TradeId,
            command.GrossGem,
            command.FeeGem,
            command.SellerNetGem);
    }

    private async Task<RefundBuyerFundsResult> RefundBuyerFundsCoreAsync(
        RefundBuyerFundsCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Amount <= 0)
        {
            throw new WalletInvariantViolationException(
                $"Refund amount for trade '{command.TradeId}' must be positive.");
        }

        var buyerWallet = await LockWalletAsync(command.BuyerId, cancellationToken);
        var hold = await LockFundHoldAsync(command.TradeId, cancellationToken);

        if (hold is null)
        {
            throw new WalletInvariantViolationException(
                $"Fund hold for trade '{command.TradeId}' does not exist and cannot be refunded.");
        }

        if (hold.Status == FundHoldStatus.Settled)
        {
            throw new WalletInvariantViolationException(
                $"Fund hold for trade '{command.TradeId}' is already Settled and must never be released.");
        }

        if (hold.BuyerId != command.BuyerId || hold.Amount != command.Amount)
        {
            throw new WalletInvariantViolationException(
                $"Fund hold for trade '{command.TradeId}' does not match buyer or refund amount.");
        }

        if (hold.Status == FundHoldStatus.Released)
        {
            return new RefundBuyerFundsResult(hold.TradeId, hold.Amount);
        }

        if (hold.Status != FundHoldStatus.Held)
        {
            throw new WalletInvariantViolationException(
                $"Fund hold for trade '{command.TradeId}' has unexpected status '{hold.Status}'.");
        }

        if (buyerWallet is null || buyerWallet.HoldBalance < command.Amount)
        {
            throw new WalletInvariantViolationException(
                $"Buyer wallet '{command.BuyerId}' has insufficient held balance to release trade '{command.TradeId}'.");
        }

        var now = DateTimeOffset.UtcNow;

        buyerWallet.HoldBalance = checked(buyerWallet.HoldBalance - command.Amount);
        buyerWallet.Version = checked(buyerWallet.Version + 1);

        hold.Status = FundHoldStatus.Released;
        hold.UpdatedAt = now;

        _dbContext.WalletTransactions.Add(new WalletTransaction
        {
            EntryId = Guid.NewGuid(),
            UserId = command.BuyerId,
            OperationKey = $"release:{command.TradeId:D}",
            TradeId = command.TradeId,
            TopUpId = null,
            TotalDelta = 0L,
            HoldDelta = checked(-command.Amount),
            FeeGem = null,
            Type = WalletTransactionType.Release,
            CreatedAt = now
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new RefundBuyerFundsResult(hold.TradeId, hold.Amount);
    }

    private async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction? ownedTransaction = null;

        if (_dbContext.Database.CurrentTransaction is null && _dbContext.Database.IsRelational())
        {
            ownedTransaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            var result = await action(cancellationToken);

            if (ownedTransaction is not null)
            {
                await ownedTransaction.CommitAsync(cancellationToken);
            }

            return result;
        }
        finally
        {
            if (ownedTransaction is not null)
            {
                await ownedTransaction.DisposeAsync();
            }
        }
    }

    private Task<WalletOperationRejection?> FindRejectionAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        return _dbContext.WalletOperationRejections
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.OperationId == operationId, cancellationToken);
    }

    private async Task PersistRejectionAsync(
        Guid operationId,
        Guid tradeId,
        WalletOperationType operationType,
        string failureCode,
        CancellationToken cancellationToken)
    {
        _dbContext.WalletOperationRejections.Add(new WalletOperationRejection
        {
            OperationId = operationId,
            TradeId = tradeId,
            OperationType = operationType,
            FailureCode = failureCode,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureWalletExistsAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsSqlServer())
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                IF NOT EXISTS (SELECT 1 FROM dbo.Wallets WITH (UPDLOCK, HOLDLOCK) WHERE UserId = {userId})
                BEGIN
                    INSERT INTO dbo.Wallets (UserId, TotalBalance, HoldBalance, Version)
                    VALUES ({userId}, 0, 0, 1);
                END
                """,
                cancellationToken);
            return;
        }

        var exists = await _dbContext.Wallets
            .AnyAsync(w => w.UserId == userId, cancellationToken);

        if (!exists)
        {
            _dbContext.Wallets.Add(new Domain.Entities.Wallet
            {
                UserId = userId,
                TotalBalance = 0L,
                HoldBalance = 0L,
                Version = 1L
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private Task<Domain.Entities.Wallet?> LockWalletAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsSqlServer())
        {
            return _dbContext.Wallets
                .FromSqlInterpolated(
                    $"SELECT UserId, TotalBalance, HoldBalance, Version FROM dbo.Wallets WITH (UPDLOCK, ROWLOCK) WHERE UserId = {userId}")
                .FirstOrDefaultAsync(cancellationToken);
        }

        return _dbContext.Wallets
            .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);
    }

    private Task<FundHold?> LockFundHoldAsync(
        Guid tradeId,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsSqlServer())
        {
            return _dbContext.FundHolds
                .FromSqlInterpolated(
                    $"SELECT TradeId, BuyerId, Amount, Status, CreatedAt, UpdatedAt FROM dbo.FundHolds WITH (UPDLOCK, ROWLOCK) WHERE TradeId = {tradeId}")
                .FirstOrDefaultAsync(cancellationToken);
        }

        return _dbContext.FundHolds
            .FirstOrDefaultAsync(h => h.TradeId == tradeId, cancellationToken);
    }

    private static bool AreSettlementAmountsValid(long grossGem, long feeGem, long sellerNetGem)
    {
        if (grossGem <= 0 || feeGem < 0 || sellerNetGem < 0)
        {
            return false;
        }

        try
        {
            return checked(feeGem + sellerNetGem) == grossGem;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}