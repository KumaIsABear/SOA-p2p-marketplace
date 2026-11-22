using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wallet.API.Application.DTOs;
using Wallet.API.Application.Exceptions;
using Wallet.API.Domain.Entities;
using Wallet.API.Domain.Enums;
using Wallet.API.Domain.Errors;
using Wallet.API.Infrastructure.Options;
using Wallet.API.Infrastructure.Persistence;

namespace Wallet.API.Application.Services;

public sealed class WalletQueryService : IWalletQueryService
{
    private const string GemCurrency = "GEM";
    private const int MaxPageSize = 100;

    private readonly WalletDbContext _dbContext;
    private readonly TopUpPackagesOptions _topUpPackagesOptions;

    public WalletQueryService(
        WalletDbContext dbContext,
        IOptions<TopUpPackagesOptions> topUpPackagesOptions)
    {
        _dbContext = dbContext;
        _topUpPackagesOptions = topUpPackagesOptions.Value;
    }

    public async Task<BalanceResponse> GetOrCreateBalanceAsync(Guid userId, CancellationToken cancellationToken)
    {
        var wallet = await _dbContext.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

        if (wallet is not null)
        {
            return ToBalanceResponse(wallet);
        }

        var newWallet = new Domain.Entities.Wallet
        {
            UserId = userId,
            TotalBalance = 0L,
            HoldBalance = 0L,
            Version = 1L
        };

        _dbContext.Wallets.Add(newWallet);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return ToBalanceResponse(newWallet);
        }
        catch (DbUpdateException ex) when (IsUniqueOrPrimaryKeyViolation(ex))
        {
            _dbContext.Entry(newWallet).State = EntityState.Detached;

            var existingWallet = await _dbContext.Wallets
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

            if (existingWallet is not null)
            {
                return ToBalanceResponse(existingWallet);
            }

            throw;
        }
    }

    public async Task<WalletTransactionPageResponse> GetTransactionsAsync(
        Guid userId,
        WalletTransactionQuery query,
        CancellationToken cancellationToken)
    {
        var validationErrors = new List<string>();

        if (query.Page < 1)
        {
            validationErrors.Add("page must be greater than or equal to 1.");
        }

        if (query.PageSize < 1 || query.PageSize > MaxPageSize)
        {
            validationErrors.Add($"pageSize must be between 1 and {MaxPageSize}.");
        }

        if (query.Type.HasValue && !Enum.IsDefined(query.Type.Value))
        {
            validationErrors.Add("type is invalid.");
        }

        if (query.From.HasValue && query.To.HasValue && query.From.Value >= query.To.Value)
        {
            validationErrors.Add("from must be strictly earlier than to.");
        }

        if (validationErrors.Count > 0)
        {
            throw new WalletApiException(
                StatusCodes.Status400BadRequest,
                WalletErrorCodes.ValidationError,
                "One or more query parameters are invalid.",
                errors: validationErrors);
        }

        var baseQuery = _dbContext.WalletTransactions
            .AsNoTracking()
            .Where(t => t.UserId == userId);

        if (query.Type.HasValue)
        {
            baseQuery = baseQuery.Where(t => t.Type == query.Type.Value);
        }

        if (query.TradeId.HasValue)
        {
            baseQuery = baseQuery.Where(t => t.TradeId == query.TradeId.Value);
        }

        if (query.From.HasValue)
        {
            var fromUtc = query.From.Value.ToUniversalTime();
            baseQuery = baseQuery.Where(t => t.CreatedAt >= fromUtc);
        }

        if (query.To.HasValue)
        {
            var toUtc = query.To.Value.ToUniversalTime();
            baseQuery = baseQuery.Where(t => t.CreatedAt < toUtc);
        }

        var totalCount = await baseQuery.LongCountAsync(cancellationToken);

        int skip;
        try
        {
            skip = checked((query.Page - 1) * query.PageSize);
        }
        catch (OverflowException)
        {
            return new WalletTransactionPageResponse([], query.Page, query.PageSize, totalCount);
        }

        var items = await baseQuery
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.EntryId)
            .Skip(skip)
            .Take(query.PageSize)
            .Select(t => new WalletTransactionDto(
                t.EntryId,
                t.UserId,
                t.TradeId,
                t.TopUpId,
                t.TotalDelta,
                t.HoldDelta,
                t.FeeGem,
                t.Type,
                t.CreatedAt.ToUniversalTime()))
            .ToListAsync(cancellationToken);

        return new WalletTransactionPageResponse(items, query.Page, query.PageSize, totalCount);
    }

    public TopUpPackagesResponse GetTopUpPackages()
    {
        var items = _topUpPackagesOptions.Items
            .Select(p => new TopUpPackageDto(p.PackageId.Trim(), p.AmountVnd, p.GemAmount))
            .ToList();

        return new TopUpPackagesResponse(items);
    }

    public async Task<WalletTradeTraceResponse> GetTradeTraceAsync(Guid tradeId, CancellationToken cancellationToken)
    {
        var holdEntity = await _dbContext.FundHolds
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.TradeId == tradeId, cancellationToken);

        var entries = await _dbContext.WalletTransactions
            .AsNoTracking()
            .Where(t => t.TradeId == tradeId)
            .OrderBy(t => t.CreatedAt)
            .ThenBy(t => t.EntryId)
            .Select(t => new WalletTransactionDto(
                t.EntryId,
                t.UserId,
                t.TradeId,
                t.TopUpId,
                t.TotalDelta,
                t.HoldDelta,
                t.FeeGem,
                t.Type,
                t.CreatedAt.ToUniversalTime()))
            .ToListAsync(cancellationToken);

        var holdDto = holdEntity is null
            ? null
            : new FundHoldDto(
                holdEntity.TradeId,
                holdEntity.BuyerId,
                holdEntity.Amount,
                holdEntity.Status,
                holdEntity.UpdatedAt.ToUniversalTime());

        var isSettled = holdEntity?.Status == FundHoldStatus.Settled
            || entries.Any(e => e.Type is WalletTransactionType.SettlementDebit or WalletTransactionType.SettlementCredit);

        var settlementStatus = isSettled ? SettlementStatus.Settled : SettlementStatus.NotSettled;

        return new WalletTradeTraceResponse(tradeId, holdDto, settlementStatus, entries);
    }

    public async Task<WalletSupplyResponse> GetSupplyAsync(CancellationToken cancellationToken)
    {
        var totalGem = await _dbContext.Wallets
            .AsNoTracking()
            .SumAsync(w => (long?)w.TotalBalance, cancellationToken) ?? 0L;

        var heldGem = await _dbContext.Wallets
            .AsNoTracking()
            .SumAsync(w => (long?)w.HoldBalance, cancellationToken) ?? 0L;

        var availableGem = checked(totalGem - heldGem);

        return new WalletSupplyResponse(totalGem, heldGem, availableGem, DateTimeOffset.UtcNow);
    }

    private static BalanceResponse ToBalanceResponse(Domain.Entities.Wallet wallet)
    {
        var availableBalance = checked(wallet.TotalBalance - wallet.HoldBalance);
        return new BalanceResponse(
            wallet.UserId,
            GemCurrency,
            wallet.TotalBalance,
            wallet.HoldBalance,
            availableBalance,
            wallet.Version);
    }

    private static bool IsUniqueOrPrimaryKeyViolation(DbUpdateException ex)
    {
        return ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2627 || sqlEx.Number == 2601);
    }
}