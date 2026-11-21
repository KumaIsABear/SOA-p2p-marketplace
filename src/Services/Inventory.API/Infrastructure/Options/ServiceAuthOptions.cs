namespace Inventory.API.Infrastructure.Options;

public sealed class ServiceAuthOptions
{
    public const string SectionName = "ServiceAuth";

    public Dictionary<string, AllowedCallerOptions> AllowedCallers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AllowedCallerOptions
{
    public string Key { get; set; } = string.Empty;
}