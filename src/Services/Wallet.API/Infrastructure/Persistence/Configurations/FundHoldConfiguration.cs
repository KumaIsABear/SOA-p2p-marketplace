using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.API.Domain.Entities;

namespace Wallet.API.Infrastructure.Persistence.Configurations;

public sealed class FundHoldConfiguration : IEntityTypeConfiguration<FundHold>
{
    public void Configure(EntityTypeBuilder<FundHold> builder)
    {
        builder.ToTable("FundHolds", table =>
        {
            table.HasCheckConstraint("CK_FundHolds_Amount_Positive", "[Amount] > 0");
            table.HasCheckConstraint("CK_FundHolds_Status_Valid", "[Status] IN ('Held', 'Settled', 'Released')");
        });

        builder.HasKey(h => h.TradeId);
        builder.Property(h => h.TradeId).ValueGeneratedNever();

        builder.Property(h => h.BuyerId).IsRequired();
        builder.Property(h => h.Amount).IsRequired();

        builder.Property(h => h.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(h => h.CreatedAt).IsRequired();
        builder.Property(h => h.UpdatedAt).IsRequired();

        builder.HasOne(h => h.BuyerWallet)
            .WithMany(w => w.FundHolds)
            .HasForeignKey(h => h.BuyerId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(h => h.BuyerId)
            .HasDatabaseName("IX_FundHolds_BuyerId");
    }
}