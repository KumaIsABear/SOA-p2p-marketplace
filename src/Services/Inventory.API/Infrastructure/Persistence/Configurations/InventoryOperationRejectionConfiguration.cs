using Inventory.API.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.API.Infrastructure.Persistence.Configurations;

public sealed class InventoryOperationRejectionConfiguration : IEntityTypeConfiguration<InventoryOperationRejection>
{
    public void Configure(EntityTypeBuilder<InventoryOperationRejection> builder)
    {
        builder.ToTable("InventoryOperationRejections");

        builder.HasKey(x => x.OperationId);

        builder.Property(x => x.TradeId)
            .IsRequired();

        builder.Property(x => x.CommandType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.FailureCode)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => x.TradeId);
    }
}