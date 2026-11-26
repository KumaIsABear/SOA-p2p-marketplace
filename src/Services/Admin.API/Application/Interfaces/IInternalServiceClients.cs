using Admin.API.Application.DTOs;

namespace Admin.API.Application.Interfaces;

public enum InternalCallOutcome {
    Success,
    NotFound,
    BadRequest,
    Conflict,
    Unavailable
}

public sealed record InternalCallResult<T>(
    InternalCallOutcome Outcome,
    T? Value = default,
    string? ErrorCode = null,
    string? ErrorDetail = null
);

public interface IAuthInternalClient {
    Task<InternalCallResult<AccessStatusDto>> UpdateUserStatusAsync(
        Guid userId,
        InternalModerationRequest request,
        string correlationId,
        CancellationToken cancellationToken
    );
}

public interface IWalletInternalClient {
    Task<InternalCallResult<WalletSupplyDto>> GetSupplyAsync(string correlationId, CancellationToken cancellationToken);
    Task<InternalCallResult<WalletTradeTraceDto>> GetTradeTraceAsync(Guid tradeId, string correlationId, CancellationToken cancellationToken);
}

public interface IInventoryInternalClient {
    Task<InternalCallResult<ItemTransferTraceDto>> GetTradeTransferTraceAsync(Guid tradeId, string correlationId, CancellationToken cancellationToken);
}

public interface IMarketplaceInternalClient {
    Task<InternalCallResult<MarketListingStatsDto>> GetStatsAsync(string correlationId, CancellationToken cancellationToken);
    Task<InternalCallResult<MarketTradeTraceDto>> GetTradeTraceAsync(Guid tradeId, string correlationId, CancellationToken cancellationToken);
    Task<InternalCallResult<TradePageDto>> SearchTradesAsync(
        Guid userId,
        string? status,
        int page,
        int pageSize,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string correlationId,
        CancellationToken cancellationToken
    );
}
