namespace Gateway.API.Application.Options;

public sealed class RateLimitBucketOptions {
    public int PermitLimit { get; init; } = 30;
    public int WindowSeconds { get; init; } = 60;
}

public sealed class RateLimitingOptions {
    public const string SectionName = "RateLimiting";

    public RateLimitBucketOptions Auth { get; init; } = new() { PermitLimit = 30, WindowSeconds = 60 };
    public RateLimitBucketOptions TopUp { get; init; } = new() { PermitLimit = 20, WindowSeconds = 60 };
    public RateLimitBucketOptions Purchase { get; init; } = new() { PermitLimit = 30, WindowSeconds = 60 };
}
