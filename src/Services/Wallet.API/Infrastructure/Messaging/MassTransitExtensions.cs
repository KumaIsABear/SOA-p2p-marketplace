using Marketplace.Contracts.V1;
using MassTransit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wallet.API.Application.Consumers;
using Wallet.API.Infrastructure.Options;
using Wallet.API.Infrastructure.Persistence;

namespace Wallet.API.Infrastructure.Messaging;

public static class MassTransitExtensions
{
    private static readonly HashSet<int> TransientSqlErrorNumbers =
    [
        -2,
        20,
        53,
        64,
        233,
        1205,
        1222,
        4060,
        10053,
        10054,
        10060,
        10928,
        10929,
        40197,
        40501,
        40613,
        49918,
        49919,
        49920
    ];

    public static IServiceCollection AddWalletMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var rabbitMq = configuration
            .GetSection(RabbitMqOptions.SectionName)
            .Get<RabbitMqOptions>() ?? new RabbitMqOptions();

        services.Configure<MassTransitHostOptions>(options =>
        {
            options.WaitUntilStarted = false;
            options.StartTimeout = TimeSpan.FromSeconds(30);
            options.StopTimeout = TimeSpan.FromSeconds(30);
        });

        services.AddMassTransit(x =>
        {
            x.AddConsumer<HoldBuyerFundsConsumer>();
            x.AddConsumer<SettleTradeConsumer>();
            x.AddConsumer<RefundBuyerFundsConsumer>();

            x.AddEntityFrameworkOutbox<WalletDbContext>(o =>
            {
                o.UseSqlServer();
                o.UseBusOutbox();
                o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
            });

            x.UsingRabbitMq((context, cfg) =>
            {
                var host = string.IsNullOrWhiteSpace(rabbitMq.Host) ? "localhost" : rabbitMq.Host.Trim();
                var port = rabbitMq.Port is > 0 and <= ushort.MaxValue ? (ushort)rabbitMq.Port : (ushort)5672;
                var virtualHost = string.IsNullOrWhiteSpace(rabbitMq.VirtualHost)
                    ? "marketplace"
                    : rabbitMq.VirtualHost.Trim();

                cfg.Host(host, port, virtualHost, h =>
                {
                    if (!string.IsNullOrWhiteSpace(rabbitMq.Username))
                    {
                        h.Username(rabbitMq.Username);
                    }

                    if (!string.IsNullOrEmpty(rabbitMq.Password))
                    {
                        h.Password(rabbitMq.Password);
                    }
                });

                cfg.ReceiveEndpoint(QueueNames.WalletCommands, e =>
                {
                    e.Durable = true;
                    e.AutoDelete = false;
                    e.PrefetchCount = 16;

                    e.UseMessageRetry(r =>
                    {
                        r.Handle<DbUpdateConcurrencyException>();
                        r.Handle<TimeoutException>();
                        r.Handle<SqlException>(IsTransientSqlException);
                        r.Handle<DbUpdateException>(ex =>
                            ex.InnerException is SqlException sqlEx && IsTransientSqlException(sqlEx));
                        r.Exponential(
                            retryLimit: 5,
                            minInterval: TimeSpan.FromMilliseconds(200),
                            maxInterval: TimeSpan.FromSeconds(5),
                            intervalDelta: TimeSpan.FromMilliseconds(300));
                    });

                    e.UseEntityFrameworkOutbox<WalletDbContext>(context);

                    e.ConfigureConsumer<HoldBuyerFundsConsumer>(context);
                    e.ConfigureConsumer<SettleTradeConsumer>(context);
                    e.ConfigureConsumer<RefundBuyerFundsConsumer>(context);
                });
            });
        });

        return services;
    }

    private static bool IsTransientSqlException(SqlException ex)
    {
        foreach (SqlError error in ex.Errors)
        {
            if (TransientSqlErrorNumbers.Contains(error.Number))
            {
                return true;
            }
        }

        return TransientSqlErrorNumbers.Contains(ex.Number);
    }
}