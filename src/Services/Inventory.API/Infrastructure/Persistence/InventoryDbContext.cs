using Inventory.API.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Inventory.API.Infrastructure.Persistence;

public sealed class InventoryDbContext : DbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options) : base(options)
    {
    }

    public DbSet<ItemTemplate> ItemTemplates => Set<ItemTemplate>();
    public DbSet<Domain.Entities.Inventory> Inventories => Set<Domain.Entities.Inventory>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<ItemLock> ItemLocks => Set<ItemLock>();
    public DbSet<ItemTransfer> ItemTransfers => Set<ItemTransfer>();
    public DbSet<HttpIdempotencyRecord> HttpIdempotencyRecords => Set<HttpIdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);
    }
}