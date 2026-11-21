using Inventory.API.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.API.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var dbOptions = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var seedOptions = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;

        if (!dbOptions.AutoMigrate && !seedOptions.Enabled)
        {
            return;
        }

        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        if (dbOptions.AutoMigrate)
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }

        if (seedOptions.Enabled)
        {
            await ItemTemplateSeeder.SeedAsync(dbContext, cancellationToken);
        }
    }
}