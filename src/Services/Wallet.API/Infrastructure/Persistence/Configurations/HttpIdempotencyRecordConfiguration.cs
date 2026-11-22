using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.API.Domain.Entities;

namespace Wallet.API.Infrastructure.Persistence.Configurations;

public sealed class HttpIdempotencyRecordConfiguration : IEntityTypeConfiguration<HttpIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<HttpIdempotencyRecord> builder)
    {
        builder.ToTable("HttpIdempotencyRecords");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.ActorId)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(r => r.Operation)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(r => r.ResourceScope)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(r => r.IdempotencyKey)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(r => r.RequestFingerprint)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(r => r.StatusCode).IsRequired();
        builder.Property(r => r.ResponseBody).IsRequired();
        builder.Property(r => r.LocationHeader).HasMaxLength(512);
        builder.Property(r => r.ResourceId);
        builder.Property(r => r.CreatedAt).IsRequired();

        builder.HasIndex(r => new { r.ActorId, r.Operation, r.ResourceScope, r.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_HttpIdempotencyRecords_ScopeKey");
    }
}