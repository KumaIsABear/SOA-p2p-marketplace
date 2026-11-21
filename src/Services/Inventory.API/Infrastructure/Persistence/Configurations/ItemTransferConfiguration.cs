using Inventory.API.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.API.Infrastructure.Persistence.Configurations;

public sealed class ItemTransferConfiguration : IEntityTypeConfiguration<ItemTransfer>
{
    public void Configure(EntityTypeBuilder<ItemTransfer> builder)
    {
        builder.ToTable("ItemTransfers", table =>
        {
            table.HasCheckConstraint("CK_ItemTransfers_Quantity_Positive", "[Quantity] >= 1");
        });

        builder.HasKey(x => x.TradeId);

        builder.Property(x => x.ItemId)
            .IsRequired();

        builder.Property(x => x.SellerId)
            .IsRequired();

        builder.Property(x => x.BuyerId)
            .IsRequired();

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(x => x.Quantity)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .IsRequired();

        builder.HasIndex(x => x.ItemId);
    }
}