namespace Marketplace.API.Application.Common;

public static class MarketplaceErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string Forbidden = "FORBIDDEN";
    public const string AccountBanned = "ACCOUNT_BANNED";
    public const string TradeRestricted = "TRADE_RESTRICTED";
    public const string DependencyUnavailable = "DEPENDENCY_UNAVAILABLE";
    public const string PricePolicyUnavailable = "PRICE_POLICY_UNAVAILABLE";
    public const string InternalError = "INTERNAL_ERROR";

    public const string ItemNotFound = "ITEM_NOT_FOUND";
    public const string ItemNotOwned = "ITEM_NOT_OWNED";
    public const string ItemNotTradable = "ITEM_NOT_TRADABLE";
    public const string ItemLocked = "ITEM_LOCKED";
    public const string QuantityMismatch = "QUANTITY_MISMATCH";
    public const string PriceOutOfBounds = "PRICE_OUT_OF_BOUNDS";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";

    public const string ListingNotFound = "LISTING_NOT_FOUND";
    public const string ListingBusy = "LISTING_BUSY";
    public const string ListingNotActive = "LISTING_NOT_ACTIVE";
}
