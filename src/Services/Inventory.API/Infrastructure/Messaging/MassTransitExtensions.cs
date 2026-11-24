using Inventory.API.Application.Consumers;
using Inventory.API.Application.Exceptions;
using Inventory.API.Infrastructure.Options;
using Inventory.API.Infrastructure.Persistence;
using Marketplace.Contracts.V1;
using MassTransit;

namespace Inventory.API.Infrastructure.Messaging;

public static class MassTransitExtensions
{
    public static IServiceCollection AddInventoryMassTransit(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.Configure<MassTransitHostOptions>(options =>
        {
            options.WaitUntilStarted = false;
        });

        var rabbitOptions = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>()
            ?? new RabbitMqOptions();

        var host = string.IsNullOrWhiteSpace(rabbitOptions.Host) ? "localhost" : rabbitOptions.Host.Trim();
        var port = rabbitOptions.Port == 0 ? (ushort)5672 : rabbitOptions.Port;
        var virtualHost = string.IsNullOrWhiteSpace(rabbitOptions.VirtualHost)
            ? "marketplace"
            : rabbitOptions.VirtualHost.Trim();

        services.AddMassTransit(x =>
        {
            x.AddConsumer<TransferItemConsumer>();
            x.AddConsumer<FinalizeItemTransferConsumer>();
            x.AddConsumer<ReverseItemTransferConsumer>();
            x.AddConsumer<UnlockItemConsumer>();

            x.AddEntityFrameworkOutbox<InventoryDbContext>(o =>
            {
                o.UseSqlServer();
                o.UseBusOutbox();
                o.QueryDelay = TimeSpan.FromSeconds(1);
                o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
            });

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(host, port, virtualHost, h =>
                {
                    if (!string.IsNullOrWhiteSpace(rabbitOptions.Username))
                    {
                        h.Username(rabbitOptions.Username);
                    }

                    if (!string.IsNullOrWhiteSpace(rabbitOptions.Password))
                    {
                        h.Password(rabbitOptions.Password);
                    }
                });

                cfg.ReceiveEndpoint(QueueNames.InventoryCommands, e =>
                {
                    e.Durable = true;
                    e.AutoDelete = false;
                    e.ConfigureConsumeTopology = true;

                    e.UseMessageRetry(r =>
                    {
                        r.Ignore<InventoryInvariantViolationException>();
                        r.Ignore<ArgumentException>();
                        r.Exponential(
                            retryLimit: 5,
                            minInterval: TimeSpan.FromMilliseconds(200),
                            maxInterval: TimeSpan.FromSeconds(5),
                            intervalDelta: TimeSpan.FromMilliseconds(500));
                    });

                    e.UseEntityFrameworkOutbox<InventoryDbContext>(context);

                    e.ConfigureConsumer<TransferItemConsumer>(context);
                    e.ConfigureConsumer<FinalizeItemTransferConsumer>(context);
                    e.ConfigureConsumer<ReverseItemTransferConsumer>(context);
                    e.ConfigureConsumer<UnlockItemConsumer>(context);
                });
            });
        });

        return services;
    }
}