namespace Marketplace.Contracts.V1;

public static class QueueNames
{
    public const string WalletCommands = "wallet-commands";
    public const string InventoryCommands = "inventory-commands";
    public const string MarketplaceSaga = "marketplace-saga";
    public const string MarketplaceProjection = "marketplace-projection";
    public const string AdminAnalytics = "admin-analytics";

    public static readonly Uri WalletCommandsUri = new($"queue:{WalletCommands}");
    public static readonly Uri InventoryCommandsUri = new($"queue:{InventoryCommands}");
    public static readonly Uri MarketplaceSagaUri = new($"queue:{MarketplaceSaga}");
    public static readonly Uri MarketplaceProjectionUri = new($"queue:{MarketplaceProjection}");
    public static readonly Uri AdminAnalyticsUri = new($"queue:{AdminAnalytics}");
}
