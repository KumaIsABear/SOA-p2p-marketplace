using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Admin.API.Infrastructure.Persistence;

public sealed class AdminDbContextFactory : IDesignTimeDbContextFactory<AdminDbContext> {
    public AdminDbContext CreateDbContext(string[] args) {
        var optionsBuilder = new DbContextOptionsBuilder<AdminDbContext>();
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=localhost,1433;Database=AdminAnalyticsDb;User Id=admin_runtime;Password=DesignTimeOnly_123!;Encrypt=True;TrustServerCertificate=True;";

        optionsBuilder.UseSqlServer(connectionString);
        return new AdminDbContext(optionsBuilder.Options);
    }
}
