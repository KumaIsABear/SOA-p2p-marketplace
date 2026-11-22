using Wallet.API.Application.DTOs;

namespace Wallet.API.Application.Services;

public interface IWalletQueryService
{
    Task<BalanceResponse> GetOrCreateBalanceAsync(Guid userId, CancellationToken cancellationToken);
    Task<WalletTransactionPageResponse> GetTransactionsAsync(Guid userId, WalletTransactionQuery query, CancellationToken cancellationToken);
    TopUpPackagesResponse GetTopUpPackages();
    Task<WalletTradeTraceResponse> GetTradeTraceAsync(Guid tradeId, CancellationToken cancellationToken);
    Task<WalletSupplyResponse> GetSupplyAsync(CancellationToken cancellationToken);
}