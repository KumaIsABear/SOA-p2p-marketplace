using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Wallet.API.Infrastructure.Persistence.Configurations;

public sealed class WalletConfiguration : IEntityTypeConfiguration<Domain.Entities.Wallet>
{
    public void Configure(EntityTypeBuilder<Domain.Entities.Wallet> builder)
    {
        builder.ToTable("Wallets", table =>
        {
            table.HasCheckConstraint("CK_Wallets_TotalBalance_NonNegative", "[TotalBalance] >= 0");
            table.HasCheckConstraint("CK_Wallets_HoldBalance_NonNegative", "[HoldBalance] >= 0");
            table.HasCheckConstraint("CK_Wallets_TotalBalance_Gte_HoldBalance", "[TotalBalance] >= [HoldBalance]");
            table.HasCheckConstraint("CK_Wallets_Version_Positive", "[Version] >= 1");
        });

        builder.HasKey(w => w.UserId);
        builder.Property(w => w.UserId).ValueGeneratedNever();

        builder.Property(w => w.TotalBalance)
            .IsRequired()
            .HasDefaultValue(0L);

        builder.Property(w => w.HoldBalance)
            .IsRequired()
            .HasDefaultValue(0L);

        builder.Property(w => w.Version)
            .IsRequired()
            .HasDefaultValue(1L)
            .IsConcurrencyToken();

        builder.Ignore(w => w.AvailableBalance);
    }
}