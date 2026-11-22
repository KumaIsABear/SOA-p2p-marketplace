using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wallet.API.Infrastructure.Options;

namespace Wallet.API.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseInitializer));

        if (!options.AutoMigrate)
        {
            logger.LogInformation("Database auto-migration is disabled.");
            return;
        }

        var dbContext = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        logger.LogInformation("Applying pending WalletDb migrations...");
        await dbContext.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("WalletDb migrations applied successfully.");
    }
}