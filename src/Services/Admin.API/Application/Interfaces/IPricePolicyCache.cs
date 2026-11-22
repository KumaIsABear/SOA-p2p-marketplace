using Marketplace.Contracts.V1;

namespace Admin.API.Application.Interfaces;

public interface IPricePolicyCache {
    Task SetAsync(PricePolicySnapshot snapshot, CancellationToken cancellationToken);
}
