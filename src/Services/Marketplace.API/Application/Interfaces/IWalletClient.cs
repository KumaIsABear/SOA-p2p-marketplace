using Marketplace.API.Application.DTOs;

namespace Marketplace.API.Application.Interfaces;

public interface IWalletClient
{
    Task<WalletTradeTraceDto?> GetTradeTraceAsync(Guid tradeId, CancellationToken ct);
}
