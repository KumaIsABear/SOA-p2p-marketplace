namespace Wallet.API.Infrastructure.Options;

public sealed class ServiceAuthOptions
{
    public const string SectionName = "ServiceAuth";

    public Dictionary<string, ServiceCallerOptions> AllowedCallers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ServiceCallerOptions
{
    public string Key { get; set; } = string.Empty;
}