namespace Admin.API.Application.Options;

public sealed class ServiceAuthOptions {
    public const string SectionName = "ServiceAuth";

    public string OutgoingKey { get; init; } = string.Empty;

    public Dictionary<string, AllowedCallerOptions> AllowedCallers { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}
