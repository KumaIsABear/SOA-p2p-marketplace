namespace Auth.API.Application.Options;

public sealed class SeedOptions {
    public const string SectionName = "Seed";

    public bool Enabled { get; init; }
    public string DefaultPassword { get; init; } = string.Empty;
}
