using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.API.Domain.Entities;

namespace Wallet.API.Infrastructure.Persistence.Configurations;

public sealed class TopUpOrderConfiguration : IEntityTypeConfiguration<TopUpOrder>
{
    public void Configure(EntityTypeBuilder<TopUpOrder> builder)
    {
        builder.ToTable("TopUpOrders", table =>
        {
            table.HasCheckConstraint("CK_TopUpOrders_Provider_ZaloPay", "[Provider] = 'ZALOPAY'");
            table.HasCheckConstraint("CK_TopUpOrders_AmountVnd_Positive", "[AmountVnd] > 0");
            table.HasCheckConstraint("CK_TopUpOrders_GemAmount_Positive", "[GemAmount] > 0");
            table.HasCheckConstraint("CK_TopUpOrders_Status_Valid", "[Status] IN ('Pending', 'Paid', 'Failed', 'Expired')");
        });

        builder.HasKey(o => o.TopUpId);
        builder.Property(o => o.TopUpId).ValueGeneratedNever();

        builder.Property(o => o.UserId).IsRequired();

        builder.Property(o => o.Provider)
            .HasMaxLength(32)
            .IsRequired()
            .HasDefaultValue(TopUpOrder.DefaultProvider);

        builder.Property(o => o.AppTransId)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(o => o.PaymentUrl)
            .HasMaxLength(2048);

        builder.Property(o => o.ProviderTxnId);
        builder.Property(o => o.AmountVnd).IsRequired();
        builder.Property(o => o.GemAmount).IsRequired();

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(o => o.CreatedAt).IsRequired();
        builder.Property(o => o.PaidAt);

        builder.HasOne(o => o.Wallet)
            .WithMany(w => w.TopUpOrders)
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(o => o.AppTransId)
            .IsUnique()
            .HasDatabaseName("UX_TopUpOrders_AppTransId");

        builder.HasIndex(o => o.ProviderTxnId)
            .IsUnique()
            .HasFilter("[ProviderTxnId] IS NOT NULL")
            .HasDatabaseName("UX_TopUpOrders_ProviderTxnId");

        builder.HasIndex(o => new { o.UserId, o.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_TopUpOrders_UserId_CreatedAt");

        builder.HasIndex(o => new { o.Status, o.CreatedAt })
            .HasDatabaseName("IX_TopUpOrders_Status_CreatedAt");
    }
}