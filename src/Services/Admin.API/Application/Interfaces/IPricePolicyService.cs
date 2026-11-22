using Admin.API.Application.Common;
using Admin.API.Application.DTOs;
using Marketplace.Contracts.V1;

namespace Admin.API.Application.Interfaces;

public interface IPricePolicyService {
    Task<AdminResult<PricePolicyPageDto>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<AdminResult<PricePolicySnapshot>> UpsertAsync(
        Guid templateId,
        Guid actorId,
        string idempotencyKey,
        PricePolicyRequest request,
        CancellationToken cancellationToken
    );

    Task<AdminResult<PricePolicySnapshot>> GetAndRefreshAsync(Guid templateId, CancellationToken cancellationToken);

    Task WarmCacheAsync(CancellationToken cancellationToken);
}
