using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Wallet.API.Domain.Entities;
using Wallet.API.Domain.Enums;
using Wallet.API.Infrastructure.Persistence;

namespace Wallet.API.Application.Services;

public sealed class TopUpSettlementService : ITopUpSettlementService
{
    private readonly WalletDbContext _dbContext;
    private readonly ILogger<TopUpSettlementService> _logger;

    public TopUpSettlementService(
        WalletDbContext dbContext,
        ILogger<TopUpSettlementService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public Task<TopUpSettlementResult> SettleTopUpAsync(
        TopUpSettlementRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteInTransactionAsync(
            ct => SettleTopUpCoreAsync(request, ct),
            cancellationToken);
    }

    public Task<bool> MarkTerminalFailureAsync(
        Guid topUpId,
        TopUpOrderStatus terminalStatus,
        CancellationToken cancellationToken)
    {
        if (terminalStatus is not (TopUpOrderStatus.Failed or TopUpOrderStatus.Expired))
        {
            throw new ArgumentOutOfRangeException(
                nameof(terminalStatus),
                terminalStatus,
                "Terminal failure status must be Failed or Expired.");
        }

        return ExecuteInTransactionAsync(
            ct => MarkTerminalFailureCoreAsync(topUpId, terminalStatus, ct),
            cancellationToken);
    }

    private async Task<TopUpSettlementResult> SettleTopUpCoreAsync(
        TopUpSettlementRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AppTransId) || request.ZpTransId <= 0 || request.AmountVnd <= 0)
        {
            _logger.LogWarning(
                "Rejected top-up settlement due to invalid provider fields. Source={Source}, AppTransId={AppTransId}, ZpTransId={ZpTransId}, AmountVnd={AmountVnd}.",
                request.Source,
                request.AppTransId,
                request.ZpTransId,
                request.AmountVnd);

            return new TopUpSettlementResult(
                TopUpSettlementOutcome.Discrepancy,
                null,
                "Invalid provider settlement fields.");
        }

        var order = await LockTopUpOrderByAppTransIdAsync(request.AppTransId, cancellationToken);
        if (order is null)
        {
            _logger.LogWarning(
                "Top-up order not found during settlement. Source={Source}, AppTransId={AppTransId}, ZpTransId={ZpTransId}.",
                request.Source,
                request.AppTransId,
                request.ZpTransId);

            return new TopUpSettlementResult(
                TopUpSettlementOutcome.OrderNotFound,
                null,
                "Top-up order not found.");
        }

        if (request.ExpectedUserId.HasValue && order.UserId != request.ExpectedUserId.Value)
        {
            _logger.LogWarning(
                "Top-up settlement user mismatch. Source={Source}, TopUpId={TopUpId}, AppTransId={AppTransId}, ExpectedUserId={ExpectedUserId}, OrderUserId={OrderUserId}.",
                request.Source,
                order.TopUpId,
                order.AppTransId,
                request.ExpectedUserId.Value,
                order.UserId);

            return new TopUpSettlementResult(
                TopUpSettlementOutcome.Discrepancy,
                order.TopUpId,
                "User mismatch.");
        }

        if (order.AmountVnd != request.AmountVnd)
        {
            _logger.LogWarning(
                "Top-up settlement amount mismatch. Source={Source}, TopUpId={TopUpId}, AppTransId={AppTransId}, ExpectedAmountVnd={ExpectedAmountVnd}, ProviderAmountVnd={ProviderAmountVnd}.",
                request.Source,
                order.TopUpId,
                order.AppTransId,
                order.AmountVnd,
                request.AmountVnd);

            return new TopUpSettlementResult(
                TopUpSettlementOutcome.Discrepancy,
                order.TopUpId,
                "Amount mismatch.");
        }

        if (order.Status == TopUpOrderStatus.Paid)
        {
            if (order.ProviderTxnId == request.ZpTransId)
            {
                return new TopUpSettlementResult(
                    TopUpSettlementOutcome.Duplicate,
                    order.TopUpId,
                    "Top-up order already settled with matching provider transaction.");
            }

            _logger.LogWarning(
                "Top-up settlement provider transaction mismatch on already Paid order. Source={Source}, TopUpId={TopUpId}, AppTransId={AppTransId}, StoredZpTransId={StoredZpTransId}, IncomingZpTransId={IncomingZpTransId}.",
                request.Source,
                order.TopUpId,
                order.AppTransId,
                order.ProviderTxnId,
                request.ZpTransId);

            return new TopUpSettlementResult(
                TopUpSettlementOutcome.Discrepancy,
                order.TopUpId,
                "Conflicting provider transaction ID on already paid order.");
        }

        if (order.Status != TopUpOrderStatus.Pending)
        {
            _logger.LogWarning(
                "Top-up settlement received for non-Pending order. Source={Source}, TopUpId={TopUpId}, AppTransId={AppTransId}, CurrentStatus={CurrentStatus}, ZpTransId={ZpTransId}.",
                request.Source,
                order.TopUpId,
                order.AppTransId,
                order.Status,
                request.ZpTransId);

            return new TopUpSettlementResult(
                TopUpSettlementOutcome.Discrepancy,
                order.TopUpId,
                $"Order is in terminal status '{order.Status}' and requires manual review.");
        }

        var duplicateProviderTxnOnOtherOrder = await _dbContext.TopUpOrders
            .AsNoTracking()
            .AnyAsync(
                o => o.ProviderTxnId == request.ZpTransId && o.TopUpId != order.TopUpId,
                cancellationToken);

        if (duplicateProviderTxnOnOtherOrder)
        {
            _logger.LogWarning(
                "Top-up settlement rejected because ZpTransId is already linked to another order. Source={Source}, TopUpId={TopUpId}, AppTransId={AppTransId}, ZpTransId={ZpTransId}.",
                request.Source,
                order.TopUpId,
                order.AppTransId,
                request.ZpTransId);

            return new TopUpSettlementResult(
                TopUpSettlementOutcome.Discrepancy,
                order.TopUpId,
                "Provider transaction ID already belongs to another order.");
        }

        var operationKey = $"topup:{order.TopUpId:D}";
        var existingLedgerEntry = await _dbContext.WalletTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.OperationKey == operationKey, cancellationToken);

        if (existingLedgerEntry is not null)
        {
            _logger.LogWarning(
                "Top-up ledger entry already exists while order is still Pending. Source={Source}, TopUpId={TopUpId}, OperationKey={OperationKey}.",
                request.Source,
                order.TopUpId,
                operationKey);

            return new TopUpSettlementResult(
                TopUpSettlementOutcome.Discrepancy,
                order.TopUpId,
                "Ledger entry already exists for pending order.");
        }

        await EnsureWalletExistsAsync(order.UserId, cancellationToken);
        var wallet = await LockWalletAsync(order.UserId, cancellationToken);
        if (wallet is null)
        {
            throw new InvalidOperationException(
                $"Wallet '{order.UserId:D}' could not be locked for top-up '{order.TopUpId:D}'.");
        }

        var now = DateTimeOffset.UtcNow;

        wallet.TotalBalance = checked(wallet.TotalBalance + order.GemAmount);
        wallet.Version = checked(wallet.Version + 1);

        _dbContext.WalletTransactions.Add(new WalletTransaction
        {
            EntryId = Guid.NewGuid(),
            UserId = order.UserId,
            OperationKey = operationKey,
            TradeId = null,
            TopUpId = order.TopUpId,
            TotalDelta = order.GemAmount,
            HoldDelta = 0L,
            FeeGem = null,
            Type = WalletTransactionType.TopUp,
            CreatedAt = now
        });

        order.Status = TopUpOrderStatus.Paid;
        order.ProviderTxnId = request.ZpTransId;
        order.PaidAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Settled top-up order successfully. Source={Source}, TopUpId={TopUpId}, AppTransId={AppTransId}, UserId={UserId}, GemAmount={GemAmount}, ZpTransId={ZpTransId}.",
            request.Source,
            order.TopUpId,
            order.AppTransId,
            order.UserId,
            order.GemAmount,
            request.ZpTransId);

        return new TopUpSettlementResult(
            TopUpSettlementOutcome.Settled,
            order.TopUpId,
            "Settled.");
    }

    private async Task<bool> MarkTerminalFailureCoreAsync(
        Guid topUpId,
        TopUpOrderStatus terminalStatus,
        CancellationToken cancellationToken)
    {
        var order = await LockTopUpOrderByIdAsync(topUpId, cancellationToken);
        if (order is null || order.Status != TopUpOrderStatus.Pending)
        {
            return false;
        }

        order.Status = terminalStatus;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Marked pending top-up order as {TerminalStatus}. TopUpId={TopUpId}, AppTransId={AppTransId}.",
            terminalStatus,
            order.TopUpId,
            order.AppTransId);

        return true;
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

    private Task<TopUpOrder?> LockTopUpOrderByAppTransIdAsync(
        string appTransId,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsSqlServer())
        {
            return _dbContext.TopUpOrders
                .FromSqlInterpolated(
                    $"""
                    SELECT TopUpId, UserId, Provider, AppTransId, PaymentUrl, ProviderTxnId, AmountVnd, GemAmount, Status, CreatedAt, PaidAt
                    FROM dbo.TopUpOrders WITH (UPDLOCK, ROWLOCK)
                    WHERE AppTransId = {appTransId}
                    """)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return _dbContext.TopUpOrders
            .FirstOrDefaultAsync(o => o.AppTransId == appTransId, cancellationToken);
    }

    private Task<TopUpOrder?> LockTopUpOrderByIdAsync(
        Guid topUpId,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsSqlServer())
        {
            return _dbContext.TopUpOrders
                .FromSqlInterpolated(
                    $"""
                    SELECT TopUpId, UserId, Provider, AppTransId, PaymentUrl, ProviderTxnId, AmountVnd, GemAmount, Status, CreatedAt, PaidAt
                    FROM dbo.TopUpOrders WITH (UPDLOCK, ROWLOCK)
                    WHERE TopUpId = {topUpId}
                    """)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return _dbContext.TopUpOrders
            .FirstOrDefaultAsync(o => o.TopUpId == topUpId, cancellationToken);
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
}