namespace Gateway.API.Application.Options;

public sealed class ServiceAuthOptions {
    public const string SectionName = "ServiceAuth";

    public string OutgoingKey { get; init; } = string.Empty;
}
