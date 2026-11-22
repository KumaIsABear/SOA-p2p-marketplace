using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Marketplace.API.Infrastructure.Persistence;

public sealed class MarketplaceDbContextFactory : IDesignTimeDbContextFactory<MarketplaceDbContext>
{
    public MarketplaceDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=localhost,1433;Database=MarketplaceWriteDb;User Id=sa;Password=DesignTimeOnly!123;Encrypt=True;TrustServerCertificate=True;";

        var optionsBuilder = new DbContextOptionsBuilder<MarketplaceDbContext>();
        optionsBuilder.UseSqlServer(conn);

        return new MarketplaceDbContext(optionsBuilder.Options);
    }
}
