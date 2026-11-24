using Marketplace.Contracts.V1;

namespace Marketplace.API.Application.Interfaces;

public interface IPricePolicyProvider
{
    Task<PricePolicySnapshot> GetRequiredPolicyAsync(Guid templateId, CancellationToken ct);
}
