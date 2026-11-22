using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.API.Domain.Entities;

namespace Wallet.API.Infrastructure.Persistence.Configurations;

public sealed class WalletTransactionConfiguration : IEntityTypeConfiguration<WalletTransaction>
{
    public void Configure(EntityTypeBuilder<WalletTransaction> builder)
    {
        builder.ToTable("WalletTransactions", table =>
        {
            table.HasCheckConstraint(
                "CK_WalletTransactions_Type_Valid",
                "[Type] IN ('TopUp', 'Hold', 'SettlementDebit', 'SettlementCredit', 'Release', 'TaxBurn')");
            table.HasCheckConstraint(
                "CK_WalletTransactions_FeeGem_TaxBurnOnly",
                "([Type] = 'TaxBurn' AND [FeeGem] IS NOT NULL AND [FeeGem] >= 0) OR ([Type] <> 'TaxBurn' AND [FeeGem] IS NULL)");
        });

        builder.HasKey(t => t.EntryId);
        builder.Property(t => t.EntryId).ValueGeneratedNever();

        builder.Property(t => t.UserId).IsRequired();

        builder.Property(t => t.OperationKey)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(t => t.TradeId);
        builder.Property(t => t.TopUpId);
        builder.Property(t => t.TotalDelta).IsRequired();
        builder.Property(t => t.HoldDelta).IsRequired();
        builder.Property(t => t.FeeGem);

        builder.Property(t => t.Type)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(t => t.CreatedAt).IsRequired();

        builder.HasOne(t => t.Wallet)
            .WithMany(w => w.Transactions)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(t => t.TopUpOrder)
            .WithMany(o => o.Transactions)
            .HasForeignKey(t => t.TopUpId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(t => t.OperationKey)
            .IsUnique()
            .HasDatabaseName("UX_WalletTransactions_OperationKey");

        builder.HasIndex(t => new { t.UserId, t.CreatedAt, t.EntryId })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_WalletTransactions_UserId_CreatedAt_EntryId");

        builder.HasIndex(t => t.TradeId)
            .HasDatabaseName("IX_WalletTransactions_TradeId");

        builder.HasIndex(t => t.TopUpId)
            .HasDatabaseName("IX_WalletTransactions_TopUpId");
    }
}