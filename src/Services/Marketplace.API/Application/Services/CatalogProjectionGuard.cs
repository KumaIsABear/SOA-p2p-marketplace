using Marketplace.Contracts.V1;

namespace Marketplace.API.Domain.Services;

public sealed record CatalogProjectionState(
    Guid ListingId,
    long Version,
    ListingStatus Status
);

public sealed record CatalogProjectionDecision(
    bool Applied,
    CatalogProjectionState State
);

public static class CatalogProjectionGuard
{
    public static readonly string[] TerminalStatusNames =
    [
        nameof(ListingStatus.Sold),
        nameof(ListingStatus.Cancelled)
    ];

    public static bool IsTerminal(ListingStatus status) =>
        status is ListingStatus.Sold or ListingStatus.Cancelled;

    public static bool CanApply(CatalogProjectionState? current, ListingSnapshot incoming)
    {
        if (current is null)
            return true;

        if (IsTerminal(current.Status))
            return false;

        return incoming.Version > current.Version;
    }

    public static CatalogProjectionDecision Apply(CatalogProjectionState? current, ListingSnapshot incoming)
    {
        if (!CanApply(current, incoming))
            return new CatalogProjectionDecision(false, current!);

        var next = new CatalogProjectionState(incoming.ListingId, incoming.Version, incoming.Status);
        return new CatalogProjectionDecision(true, next);
    }
}
