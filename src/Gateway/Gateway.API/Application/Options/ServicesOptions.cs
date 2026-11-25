namespace Gateway.API.Application.Options;

public sealed class ServiceEndpointOptions {
    public string BaseUrl { get; init; } = string.Empty;
}

public sealed class ServicesOptions {
    public const string SectionName = "Services";

    public ServiceEndpointOptions Auth { get; init; } = new();
    public ServiceEndpointOptions Wallet { get; init; } = new();
    public ServiceEndpointOptions Inventory { get; init; } = new();
    public ServiceEndpointOptions Marketplace { get; init; } = new();
    public ServiceEndpointOptions Admin { get; init; } = new();
}
