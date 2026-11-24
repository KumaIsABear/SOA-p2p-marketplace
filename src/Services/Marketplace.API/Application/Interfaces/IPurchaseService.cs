using Marketplace.API.Application.DTOs;

namespace Marketplace.API.Application.Interfaces;

public interface IPurchaseService
{
    Task<PurchaseAcceptedDto> PurchaseListingAsync(
        Guid buyerId,
        Guid listingId,
        string idempotencyKey,
        CancellationToken ct
    );
}
