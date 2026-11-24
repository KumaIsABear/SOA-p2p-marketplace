namespace Inventory.API.Infrastructure.Options;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = "localhost";
    public ushort Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "marketplace";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}