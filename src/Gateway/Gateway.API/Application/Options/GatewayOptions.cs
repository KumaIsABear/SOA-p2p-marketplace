namespace Gateway.API.Application.Options;

public sealed class GatewayOptions {
    public const string SectionName = "Gateway";

    public int AuthStatusTimeoutSeconds { get; init; } = 5;
    public string[] InternalHosts { get; init; } = ["localhost", "127.0.0.1", "::1", "gateway"];
}
