namespace Marketplace.API.Application.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; init; } = "marketplace-auth";
    public string Audience { get; init; } = "marketplace-api";
    public string SigningKey { get; init; } = string.Empty;
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public bool AutoMigrate { get; init; } = true;
}

public sealed class EconomyOptions
{
    public const string SectionName = "Economy";
    public int FeeBasisPoints { get; init; } = 500;
    public int ListingLockTtlSeconds { get; init; } = 20;
}

public sealed class InternalServicesOptions
{
    public const string SectionName = "InternalServices";
    public string ServiceKey { get; init; } = string.Empty;
    public string AuthBaseUrl { get; init; } = "http://auth-api:8080";
    public string InventoryBaseUrl { get; init; } = "http://inventory-api:8080";
    public string AdminBaseUrl { get; init; } = "http://admin-api:8080";
    public string WalletBaseUrl { get; init; } = "http://wallet-api:8080";
    public int TimeoutSeconds { get; init; } = 10;
}

public sealed class MongoDbOptions
{
    public const string SectionName = "MongoDb";
    public string ConnectionString { get; init; } = string.Empty;
    public string DatabaseName { get; init; } = "MarketplaceReadDb";
    public string CatalogCollectionName { get; init; } = "Marketplace_Catalog";
}

public sealed class AllowedCallerOptions
{
    public string Key { get; init; } = string.Empty;
}

public sealed class ServiceAuthOptions
{
    public const string SectionName = "ServiceAuth";

    public Dictionary<string, AllowedCallerOptions> AllowedCallers { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class RecoveryOptions
{
    public const string SectionName = "Recovery";

    public int PollIntervalSeconds { get; init; } = 10;
    public int PendingLockGraceSeconds { get; init; } = 30;
    public int SagaStaleSeconds { get; init; } = 30;
    public int MaxSagaRetries { get; init; } = 5;
    public int BatchSize { get; init; } = 20;
}
