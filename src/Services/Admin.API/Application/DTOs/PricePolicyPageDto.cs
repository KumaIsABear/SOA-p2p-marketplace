using Marketplace.Contracts.V1;

namespace Admin.API.Application.DTOs;

public sealed record PricePolicyPageDto(
    IReadOnlyList<PricePolicySnapshot> Items,
    int Page,
    int PageSize,
    long TotalCount
);
