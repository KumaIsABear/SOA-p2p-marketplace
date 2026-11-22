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
