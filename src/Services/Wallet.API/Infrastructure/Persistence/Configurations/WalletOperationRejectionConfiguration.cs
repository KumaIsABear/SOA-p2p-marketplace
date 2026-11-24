using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.API.Domain.Entities;

namespace Wallet.API.Infrastructure.Persistence.Configurations;

public sealed class WalletOperationRejectionConfiguration : IEntityTypeConfiguration<WalletOperationRejection>
{
    public void Configure(EntityTypeBuilder<WalletOperationRejection> builder)
    {
        builder.ToTable("WalletOperationRejections", table =>
        {
            table.HasCheckConstraint(
                "CK_WalletOperationRejections_OperationType_Valid",
                "[OperationType] IN ('Hold', 'Settle')");
        });

        builder.HasKey(r => r.OperationId);
        builder.Property(r => r.OperationId).ValueGeneratedNever();

        builder.Property(r => r.TradeId).IsRequired();

        builder.Property(r => r.OperationType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(r => r.FailureCode)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(r => r.CreatedAt).IsRequired();

        builder.HasIndex(r => r.TradeId)
            .HasDatabaseName("IX_WalletOperationRejections_TradeId");
    }
}