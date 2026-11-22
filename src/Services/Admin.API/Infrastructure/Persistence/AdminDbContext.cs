using Admin.API.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Admin.API.Infrastructure.Persistence;

public sealed class AdminDbContext : DbContext {
    public AdminDbContext(DbContextOptions<AdminDbContext> options) : base(options) { }

    public DbSet<PricePolicy> PricePolicies => Set<PricePolicy>();
    public DbSet<PricePolicyAudit> PricePolicyAudits => Set<PricePolicyAudit>();
    public DbSet<TradeFact> TradeFacts => Set<TradeFact>();
    public DbSet<MarketDailySummary> MarketDailySummaries => Set<MarketDailySummary>();
    public DbSet<ModerationAction> ModerationActions => Set<ModerationAction>();
    public DbSet<HttpIdempotencyRecord> HttpIdempotencyRecords => Set<HttpIdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        modelBuilder.Entity<PricePolicy>(entity => {
            entity.ToTable("Price_Policies", table => {
                table.HasCheckConstraint("CK_Price_Policies_BasePrice_Positive", "[BasePrice] >= 1");
                table.HasCheckConstraint("CK_Price_Policies_BandBasisPoints_Range", "[BandBasisPoints] >= 0 AND [BandBasisPoints] <= 9999");
                table.HasCheckConstraint("CK_Price_Policies_Version_Positive", "[Version] >= 1");
            });

            entity.HasKey(p => p.TemplateId);
            entity.Property(p => p.BasePrice).IsRequired();
            entity.Property(p => p.BandBasisPoints).IsRequired();
            entity.Property(p => p.Version).IsRequired().IsConcurrencyToken();
            entity.Property(p => p.UpdatedAt).IsRequired();
        });

        modelBuilder.Entity<PricePolicyAudit>(entity => {
            entity.ToTable("Price_Policy_Audits");

            entity.HasKey(a => a.AuditId);
            entity.Property(a => a.TemplateId).IsRequired();
            entity.Property(a => a.ActorId).IsRequired();
            entity.Property(a => a.BasePrice).IsRequired();
            entity.Property(a => a.BandBasisPoints).IsRequired();
            entity.Property(a => a.Version).IsRequired();
            entity.Property(a => a.Reason).IsRequired().HasMaxLength(500);
            entity.Property(a => a.CreatedAt).IsRequired();

            entity.HasIndex(a => new { a.TemplateId, a.CreatedAt });

            entity.HasOne(a => a.Policy)
                .WithMany(p => p.Audits)
                .HasForeignKey(a => a.TemplateId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TradeFact>(entity => {
            entity.ToTable("Trade_Facts", table => {
                table.HasCheckConstraint("CK_Trade_Facts_Quantity_Positive", "[Quantity] >= 1");
                table.HasCheckConstraint("CK_Trade_Facts_GrossGem_Positive", "[GrossGem] >= 1");
                table.HasCheckConstraint("CK_Trade_Facts_FeeGem_NonNegative", "[FeeGem] >= 0");
            });

            entity.HasKey(tf => tf.TradeId);
            entity.Property(tf => tf.TemplateId).IsRequired();
            entity.Property(tf => tf.GrossGem).IsRequired();
            entity.Property(tf => tf.FeeGem).IsRequired();
            entity.Property(tf => tf.Quantity).IsRequired();
            entity.Property(tf => tf.CompletedAt).IsRequired();

            entity.HasIndex(tf => new { tf.TemplateId, tf.CompletedAt });
            entity.HasIndex(tf => tf.CompletedAt);
        });

        modelBuilder.Entity<MarketDailySummary>(entity => {
            entity.ToTable("Market_Daily_Summaries", table => {
                table.HasCheckConstraint("CK_Market_Daily_Summaries_ReferencePrice_Positive", "[ReferencePrice] >= 1");
            });

            entity.HasKey(s => new { s.Day, s.TemplateId });
            entity.Property(s => s.Day).HasColumnType("date").IsRequired();
            entity.Property(s => s.TradeCount).IsRequired();
            entity.Property(s => s.TotalGrossGem).IsRequired();
            entity.Property(s => s.TotalFeeGem).IsRequired();
            entity.Property(s => s.Volume).IsRequired();
            entity.Property(s => s.ReferencePrice).IsRequired();
            entity.Property(s => s.UpdatedAt).IsRequired();

            entity.HasIndex(s => new { s.TemplateId, s.Day });
        });

        modelBuilder.Entity<ModerationAction>(entity => {
            entity.ToTable("Moderation_Actions");

            entity.HasKey(m => m.ActionId);
            entity.Property(m => m.OperationId).IsRequired();
            entity.Property(m => m.ActorId).IsRequired();
            entity.Property(m => m.TargetUserId).IsRequired();
            entity.Property(m => m.Action).IsRequired().HasMaxLength(64);
            entity.Property(m => m.Reason).IsRequired().HasMaxLength(500);
            entity.Property(m => m.Status).IsRequired().HasMaxLength(32);
            entity.Property(m => m.CreatedAt).IsRequired();
            entity.Property(m => m.UpdatedAt).IsRequired();

            entity.HasIndex(m => m.OperationId).IsUnique();
            entity.HasIndex(m => m.TargetUserId);
        });

        modelBuilder.Entity<HttpIdempotencyRecord>(entity => {
            entity.ToTable("HttpIdempotencyRecords");

            entity.HasKey(r => r.Id);
            entity.Property(r => r.ActorId).IsRequired().HasMaxLength(100);
            entity.Property(r => r.Operation).IsRequired().HasMaxLength(100);
            entity.Property(r => r.ResourceScope).IsRequired().HasMaxLength(200);
            entity.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(128);
            entity.Property(r => r.RequestFingerprint).IsRequired().HasMaxLength(128);
            entity.Property(r => r.StatusCode).IsRequired();
            entity.Property(r => r.ResponseBody).IsRequired();
            entity.Property(r => r.CreatedAt).IsRequired();

            entity.HasIndex(r => new { r.ActorId, r.Operation, r.ResourceScope, r.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("UX_HttpIdempotencyRecords_ScopeKey");
        });
    }
}
