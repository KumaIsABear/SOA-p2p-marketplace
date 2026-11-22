using Marketplace.API.Domain.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.API.Infrastructure.Persistence;

public sealed class MarketplaceDbContext : DbContext
{
    public MarketplaceDbContext(DbContextOptions<MarketplaceDbContext> options) : base(options) { }

    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<TradeHistory> TradeHistories => Set<TradeHistory>();
    public DbSet<HttpIdempotencyRecord> HttpIdempotencyRecords => Set<HttpIdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Listing>(entity =>
        {
            entity.ToTable("Listings");
            entity.HasKey(x => x.ListingId);

            entity.Property(x => x.ItemName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Rarity).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

            entity.HasIndex(x => new { x.SellerId, x.Status, x.CreatedAt });
            entity.HasIndex(x => new { x.Status, x.UpdatedAt });
            entity.HasIndex(x => x.ItemId);
        });

        modelBuilder.Entity<TradeHistory>(entity =>
        {
            entity.ToTable("Trade_Histories");
            entity.HasKey(x => x.TradeId);

            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.FailureCode).HasMaxLength(64);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();

            entity.HasOne<Listing>()
                .WithMany()
                .HasForeignKey(x => x.ListingId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new { x.BuyerId, x.Status, x.CreatedAt });
            entity.HasIndex(x => new { x.SellerId, x.Status, x.CreatedAt });
            entity.HasIndex(x => new { x.BuyerId, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => x.ListingId)
                .IsUnique()
                .HasFilter("[Status] = 'Completed'");
        });

        modelBuilder.Entity<HttpIdempotencyRecord>(entity =>
        {
            entity.ToTable("HttpIdempotencyRecords");
            entity.HasKey(x => x.RecordId);

            entity.Property(x => x.Operation).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ResourceScope).HasMaxLength(128).IsRequired();
            entity.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
            entity.Property(x => x.RequestFingerprint).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ErrorCode).HasMaxLength(64);
            entity.Property(x => x.ErrorDetail).HasMaxLength(500);

            entity.HasIndex(x => new { x.ActorId, x.Operation, x.ResourceScope, x.IdempotencyKey })
                .IsUnique();
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
