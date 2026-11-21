using Inventory.API.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.API.Infrastructure.Persistence.Configurations;

public sealed class HttpIdempotencyRecordConfiguration : IEntityTypeConfiguration<HttpIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<HttpIdempotencyRecord> builder)
    {
        builder.ToTable("HttpIdempotencyRecords");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ActorId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Operation)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.ResourceScope)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(x => x.RequestFingerprint)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(x => x.StatusCode)
            .IsRequired();

        builder.Property(x => x.ResponseBody)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => new { x.ActorId, x.Operation, x.ResourceScope, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_HttpIdempotencyRecords_ScopeKey");
    }
}