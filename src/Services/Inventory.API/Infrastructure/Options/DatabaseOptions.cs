namespace Inventory.API.Infrastructure.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public bool AutoMigrate { get; set; }
}