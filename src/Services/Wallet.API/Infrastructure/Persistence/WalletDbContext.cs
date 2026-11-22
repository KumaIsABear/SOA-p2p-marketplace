using Microsoft.EntityFrameworkCore;
using Wallet.API.Domain.Entities;

namespace Wallet.API.Infrastructure.Persistence;

public sealed class WalletDbContext : DbContext
{
    public WalletDbContext(DbContextOptions<WalletDbContext> options)
        : base(options)
    {
    }

    public DbSet<Domain.Entities.Wallet> Wallets => Set<Domain.Entities.Wallet>();
    public DbSet<FundHold> FundHolds => Set<FundHold>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<TopUpOrder> TopUpOrders => Set<TopUpOrder>();
    public DbSet<HttpIdempotencyRecord> HttpIdempotencyRecords => Set<HttpIdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WalletDbContext).Assembly);
    }
}