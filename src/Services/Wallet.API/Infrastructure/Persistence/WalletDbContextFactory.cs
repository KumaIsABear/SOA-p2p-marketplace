using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Wallet.API.Infrastructure.Persistence;

public sealed class WalletDbContextFactory : IDesignTimeDbContextFactory<WalletDbContext>
{
    public WalletDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString =
                "Server=localhost,1433;Database=WalletDb;User Id=sa;Password=DesignTimeOnly123!;Encrypt=True;TrustServerCertificate=True;";
        }

        var optionsBuilder = new DbContextOptionsBuilder<WalletDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new WalletDbContext(optionsBuilder.Options);
    }
}