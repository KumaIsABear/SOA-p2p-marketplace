using Marketplace.API.Application.Options;
using Marketplace.API.Infrastructure.Persistence;
using Marketplace.Contracts.V1;
using MassTransit;
using Microsoft.Extensions.Options;

namespace Marketplace.API.Infrastructure.Messaging;

public static class MassTransitExtensions
{
    public static IServiceCollection AddMarketplaceMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Host), "RabbitMq:Host is required.")
            .Validate(o => o.Port > 0, "RabbitMq:Port must be positive.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.VirtualHost), "RabbitMq:VirtualHost is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Username), "RabbitMq:Username is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Password), "RabbitMq:Password is required.")
            .ValidateOnStart();

        MapCommandEndpoints();

        services.AddMassTransit(bus =>
        {
            bus.AddConsumer<ItemUnlockedEventConsumer>();

            bus.AddEntityFrameworkOutbox<MarketplaceDbContext>(outbox =>
            {
                outbox.UseSqlServer();
                outbox.UseBusOutbox();
            });

            bus.UsingRabbitMq((context, cfg) =>
            {
                var options = context.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

                cfg.Host(options.Host, options.Port, options.VirtualHost, h =>
                {
                    h.Username(options.Username);
                    h.Password(options.Password);
                });

                cfg.ReceiveEndpoint(QueueNames.MarketplaceSaga, e =>
                {
                    e.Durable = true;
                    e.AutoDelete = false;
                    e.UseMessageRetry(r => r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(2)));
                    e.UseEntityFrameworkOutbox<MarketplaceDbContext>(context);
                    e.ConfigureConsumer<ItemUnlockedEventConsumer>(context);
                });
            });
        });

        return services;
    }

    private static void MapCommandEndpoints()
    {
        EndpointConvention.Map<HoldBuyerFundsCommand>(QueueNames.WalletCommandsUri);
        EndpointConvention.Map<SettleTradeCommand>(QueueNames.WalletCommandsUri);
        EndpointConvention.Map<RefundBuyerFundsCommand>(QueueNames.WalletCommandsUri);

        EndpointConvention.Map<TransferItemCommand>(QueueNames.InventoryCommandsUri);
        EndpointConvention.Map<ReverseItemTransferCommand>(QueueNames.InventoryCommandsUri);
        EndpointConvention.Map<FinalizeItemTransferCommand>(QueueNames.InventoryCommandsUri);
        EndpointConvention.Map<UnlockItemCommand>(QueueNames.InventoryCommandsUri);
    }
}
