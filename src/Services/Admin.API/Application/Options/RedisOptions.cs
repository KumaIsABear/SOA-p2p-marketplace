namespace Admin.API.Application.Options;

public sealed class RedisOptions {
    public const string SectionName = "Redis";

    public string ConnectionString { get; init; } = string.Empty;
}
