namespace Admin.API.Application.Options;

public sealed class ServiceAuthOptions {
    public const string SectionName = "ServiceAuth";

    public Dictionary<string, AllowedCallerOptions> AllowedCallers { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}
