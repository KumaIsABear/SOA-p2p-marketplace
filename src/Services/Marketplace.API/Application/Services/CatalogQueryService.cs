using Marketplace.API.Application.Common;
using Marketplace.API.Application.DTOs;
using Marketplace.API.Application.Interfaces;

namespace Marketplace.API.Application.Services;

public sealed class CatalogQueryService : ICatalogQueryService
{
    private static readonly HashSet<string> ValidSorts = new(StringComparer.Ordinal)
    {
        "newest",
        "priceAsc",
        "priceDesc"
    };

    private readonly IAuthStatusClient _authClient;
    private readonly ICatalogProjectionStore _catalogStore;

    public CatalogQueryService(IAuthStatusClient authClient, ICatalogProjectionStore catalogStore)
    {
        _authClient = authClient;
        _catalogStore = catalogStore;
    }

    public async Task<ListingPageDto> SearchListingsAsync(
        Guid requesterId,
        string? q,
        string? category,
        string? rarity,
        long? minPrice,
        long? maxPrice,
        string? sort,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var errors = new List<string>();

        var trimmedQ = q?.Trim();
        if (trimmedQ is not null && trimmedQ.Length > 100)
            errors.Add("q must be at most 100 characters.");

        if (minPrice is < 0)
            errors.Add("minPrice must be non-negative.");

        if (maxPrice is < 0)
            errors.Add("maxPrice must be non-negative.");

        if (minPrice.HasValue && maxPrice.HasValue && minPrice.Value > maxPrice.Value)
            errors.Add("minPrice cannot be greater than maxPrice.");

        var effectiveSort = string.IsNullOrWhiteSpace(sort) ? "newest" : sort.Trim();
        if (!ValidSorts.Contains(effectiveSort))
            errors.Add("sort must be one of: newest, priceAsc, priceDesc.");

        if (page < 1)
            errors.Add("page must be at least 1.");

        if (pageSize is < 1 or > 100)
            errors.Add("pageSize must be between 1 and 100.");

        if (errors.Count > 0)
            throw new MarketplaceApiException(400, MarketplaceErrorCodes.ValidationError, "Request validation failed.", errors.ToArray());

        var access = await _authClient.GetAccessStatusAsync(requesterId, ct);
        if (string.Equals(access.Status, "Banned", StringComparison.OrdinalIgnoreCase))
            throw new MarketplaceApiException(403, MarketplaceErrorCodes.AccountBanned, "Account is banned.");

        var query = new CatalogSearchQuery(
            Query: string.IsNullOrEmpty(trimmedQ) ? null : trimmedQ,
            Category: string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
            Rarity: string.IsNullOrWhiteSpace(rarity) ? null : rarity.Trim(),
            MinPrice: minPrice,
            MaxPrice: maxPrice,
            Sort: effectiveSort,
            Page: page,
            PageSize: pageSize
        );

        return await _catalogStore.SearchActiveAsync(query, ct);
    }
}
