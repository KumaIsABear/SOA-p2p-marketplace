namespace Marketplace.API.Application.Interfaces;

public interface IDistributedListingLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(Guid listingId, TimeSpan ttl, CancellationToken ct);
}
