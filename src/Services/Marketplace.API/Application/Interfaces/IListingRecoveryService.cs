namespace Marketplace.API.Application.Interfaces;

public interface IListingRecoveryService
{
    Task<int> RecoverPendingListingsAsync(CancellationToken ct);
    Task<int> RecoverStalledSagasAsync(CancellationToken ct);
}
