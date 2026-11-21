namespace Marketplace.Contracts.V1;

public static class CacheKeys {
    public static string ListingLock(Guid listingId) => $"lock:listing:{listingId}";
    public static string PricePolicy(Guid templateId) => $"price:policy:{templateId}";
    public static string Analytics(DateOnly day, Guid templateId) => $"analytics:{day:yyyy-MM-dd}:{templateId}";
}
