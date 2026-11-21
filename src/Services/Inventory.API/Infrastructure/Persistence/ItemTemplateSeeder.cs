using Inventory.API.Domain.Entities;
using Inventory.API.Domain.Seed;
using Microsoft.EntityFrameworkCore;

namespace Inventory.API.Infrastructure.Persistence;

public static class ItemTemplateSeeder
{
    private static readonly IReadOnlyList<ItemTemplate> DefaultTemplates =
    [
        new ItemTemplate
        {
            TemplateId = ItemTemplateSeedIds.DragonFangSword,
            Name = "Dragon Fang Sword",
            Category = "Weapon",
            Rarity = "Legendary",
            Tradable = true
        },
        new ItemTemplate
        {
            TemplateId = ItemTemplateSeedIds.AegisKnightShield,
            Name = "Aegis Knight Shield",
            Category = "Armor",
            Rarity = "Epic",
            Tradable = true
        },
        new ItemTemplate
        {
            TemplateId = ItemTemplateSeedIds.GreaterHealingPotion,
            Name = "Greater Healing Potion",
            Category = "Consumable",
            Rarity = "Rare",
            Tradable = true
        },
        new ItemTemplate
        {
            TemplateId = ItemTemplateSeedIds.StarlightOre,
            Name = "Starlight Ore",
            Category = "Material",
            Rarity = "Uncommon",
            Tradable = true
        },
        new ItemTemplate
        {
            TemplateId = ItemTemplateSeedIds.SoulboundStarterBlade,
            Name = "Soulbound Starter Blade",
            Category = "Weapon",
            Rarity = "Common",
            Tradable = false
        }
    ];

    public static async Task SeedAsync(InventoryDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var existingIds = await dbContext.ItemTemplates
            .AsNoTracking()
            .Select(x => x.TemplateId)
            .ToListAsync(cancellationToken);

        var existingSet = existingIds.ToHashSet();
        var missing = DefaultTemplates
            .Where(t => !existingSet.Contains(t.TemplateId))
            .Select(t => new ItemTemplate
            {
                TemplateId = t.TemplateId,
                Name = t.Name,
                Category = t.Category,
                Rarity = t.Rarity,
                Tradable = t.Tradable
            })
            .ToList();

        if (missing.Count == 0)
        {
            return;
        }

        dbContext.ItemTemplates.AddRange(missing);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}