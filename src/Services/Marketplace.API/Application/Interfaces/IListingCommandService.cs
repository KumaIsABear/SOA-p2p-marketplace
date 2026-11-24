using Marketplace.API.Application.DTOs;
using Marketplace.Contracts.V1;

namespace Marketplace.API.Application.Interfaces;

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
