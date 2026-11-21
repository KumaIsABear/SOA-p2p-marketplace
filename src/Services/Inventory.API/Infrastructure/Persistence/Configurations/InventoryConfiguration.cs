using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.API.Infrastructure.Persistence.Configurations;

public sealed class InventoryConfiguration : IEntityTypeConfiguration<Domain.Entities.Inventory>
{
    public void Configure(EntityTypeBuilder<Domain.Entities.Inventory> builder)
    {
        builder.ToTable("Inventories", table =>
        {
            table.HasCheckConstraint("CK_Inventories_Capacity_NonNegative", "[Capacity] >= 0");
            table.HasCheckConstraint("CK_Inventories_ReservedSlots_NonNegative", "[ReservedSlots] >= 0");
        });

        builder.HasKey(x => x.UserId);

        builder.Property(x => x.Capacity)
            .IsRequired()
            .HasDefaultValue(20);

        builder.Property(x => x.ReservedSlots)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.Version)
            .IsRequired()
            .IsConcurrencyToken();
    }
}