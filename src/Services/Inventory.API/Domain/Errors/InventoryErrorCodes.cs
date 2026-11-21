namespace Inventory.API.Domain.Errors;

public static class InventoryErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string Forbidden = "FORBIDDEN";
    public const string TemplateNotFound = "TEMPLATE_NOT_FOUND";
    public const string InventoryFull = "INVENTORY_FULL";
    public const string ItemNotFound = "ITEM_NOT_FOUND";
    public const string ItemNotOwned = "ITEM_NOT_OWNED";
    public const string ItemNotTradable = "ITEM_NOT_TRADABLE";
    public const string ItemLocked = "ITEM_LOCKED";
    public const string QuantityMismatch = "QUANTITY_MISMATCH";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string ItemLockMismatch = "ITEM_LOCK_MISMATCH";
    public const string TransferPending = "TRANSFER_PENDING";
    public const string DependencyUnavailable = "DEPENDENCY_UNAVAILABLE";
    public const string InternalError = "INTERNAL_ERROR";
}