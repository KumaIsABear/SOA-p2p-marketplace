using Marketplace.API.Application.DTOs;
using Marketplace.Contracts.V1;

namespace Marketplace.API.Application.Interfaces;

public interface IAuthStatusClient
{
    Task<AuthAccessStatusDto> GetAccessStatusAsync(Guid userId, CancellationToken ct);
}

public interface IInventoryClient
{
    Task<InventoryItemDto?> GetItemAsync(Guid itemId, CancellationToken ct);
    Task<LockCallResult> LockItemAsync(LockItemRequestDto request, CancellationToken ct);
    Task<bool> TryUnlockItemAsync(Guid listingId, UnlockItemRequestDto request, CancellationToken ct);
}

public interface IPricePolicyProvider
{
    Task<PricePolicySnapshot> GetRequiredPolicyAsync(Guid templateId, CancellationToken ct);
}

public interface IDistributedListingLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(Guid listingId, TimeSpan ttl, CancellationToken ct);
}

public interface IListingCommandService
{
    Task<CreateListingOutcome> CreateListingAsync(
        Guid sellerId,
        string idempotencyKey,
        CreateListingRequest request,
        CancellationToken ct
    );

    Task<CancelListingResultDto> CancelListingAsync(
        Guid sellerId,
        Guid listingId,
        CancellationToken ct
    );

    Task<ListingDto> GetListingAsync(
        Guid requesterId,
        Guid listingId,
        CancellationToken ct
    );

    Task<ListingPageDto> GetMyListingsAsync(
        Guid sellerId,
        ListingStatus status,
        int page,
        int pageSize,
        CancellationToken ct
    );
}
