using Marketplace.Contracts.V1;

namespace Marketplace.API.Application.DTOs;

public sealed record CreateListingRequest(
    Guid ItemId,
    int Quantity,
    long UnitPrice
);

public sealed record ListingDto(
    Guid ListingId,
    Guid SellerId,
    Guid ItemId,
    Guid TemplateId,
    string ItemName,
    string Category,
    string Rarity,
    int Quantity,
    long UnitPrice,
    long GrossGem,
    ListingStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record ListingPageDto(
    IReadOnlyList<ListingDto> Items,
    int Page,
    int PageSize,
    long TotalCount
);

public sealed record CancelListingResultDto(
    Guid ListingId,
    string Status,
    string InventoryUnlockStatus
);

public sealed record CreateListingOutcome(
    int StatusCode,
    ListingDto Listing
);

public sealed record ApiProblemDetails(
    string Type,
    string Title,
    int Status,
    string Detail,
    string Instance,
    string Code,
    string TraceId,
    string[]? Errors = null
);
