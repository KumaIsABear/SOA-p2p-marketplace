using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Auth.API.Infrastructure.Persistence;

public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext> {
    public AuthDbContext CreateDbContext(string[] args) {
        var optionsBuilder = new DbContextOptionsBuilder<AuthDbContext>();
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=localhost,1433;Database=AuthDb;User Id=auth_runtime;Password=DesignTimeOnly_123!;Encrypt=True;TrustServerCertificate=True;";

        optionsBuilder.UseSqlServer(connectionString);
        return new AuthDbContext(optionsBuilder.Options);
    }
}
