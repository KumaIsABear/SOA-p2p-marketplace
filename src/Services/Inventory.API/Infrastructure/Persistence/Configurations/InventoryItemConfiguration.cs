using Inventory.API.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.API.Infrastructure.Persistence.Configurations;

public sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("InventoryItems", table =>
        {
            table.HasCheckConstraint("CK_InventoryItems_Quantity_Positive", "[Quantity] >= 1");
        });

        builder.HasKey(x => x.ItemId);

        builder.Property(x => x.Quantity)
            .IsRequired();

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(x => x.Version)
            .IsRequired()
            .IsConcurrencyToken();

        builder.HasOne(x => x.Inventory)
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Template)
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.TemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.OwnerId);
        builder.HasIndex(x => x.ListingId);
        builder.HasIndex(x => x.TradeId);
    }
}