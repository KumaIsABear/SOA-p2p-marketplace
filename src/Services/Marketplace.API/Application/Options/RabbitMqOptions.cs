namespace Marketplace.API.Application.Options;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; init; } = "rabbitmq";
    public ushort Port { get; init; } = 5672;
    public string VirtualHost { get; init; } = "marketplace";
    public string Username { get; init; } = "marketplace_service";
    public string Password { get; init; } = string.Empty;
}
