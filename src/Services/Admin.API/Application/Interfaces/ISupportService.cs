using Admin.API.Application.Common;
using Admin.API.Application.DTOs;

namespace Admin.API.Application.Interfaces;

public interface ISupportService {
    Task<AdminResult<SupportTradeTraceDto>> GetTradeTraceAsync(Guid tradeId, string correlationId, CancellationToken cancellationToken);
    Task<AdminResult<TradePageDto>> SearchTradesAsync(
        Guid? userId,
        string? status,
        int page,
        int pageSize,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string correlationId,
        CancellationToken cancellationToken
    );
    Task<AdminResult<ModerationResultDto>> ModerateUserAsync(
        Guid targetUserId,
        Guid actorId,
        string idempotencyKey,
        ModerationRequest request,
        string correlationId,
        CancellationToken cancellationToken
    );
}
