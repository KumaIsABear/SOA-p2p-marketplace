using Inventory.API.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.API.Infrastructure.Persistence.Configurations;

public sealed class ItemLockConfiguration : IEntityTypeConfiguration<ItemLock>
{
    public void Configure(EntityTypeBuilder<ItemLock> builder)
    {
        builder.ToTable("ItemLocks", table =>
        {
            table.HasCheckConstraint("CK_ItemLocks_Quantity_Positive", "[Quantity] >= 1");
        });

        builder.HasKey(x => x.ListingId);

        builder.Property(x => x.SellerId)
            .IsRequired();

        builder.Property(x => x.TemplateId)
            .IsRequired();

        builder.Property(x => x.Quantity)
            .IsRequired();

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(x => x.LockedAt)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .IsRequired();

        builder.HasOne(x => x.Item)
            .WithMany()
            .HasForeignKey(x => x.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ItemId);
    }
}