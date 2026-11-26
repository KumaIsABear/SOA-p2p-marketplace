const categories = ["Weapon", "Armor", "Consumable", "Material"];
const rarities = ["Common", "Rare", "Epic", "Legendary"];
const templateIds = [
  "30000000-0000-4000-8000-000000000001",
  "30000000-0000-4000-8000-000000000002",
  "30000000-0000-4000-8000-000000000003",
  "30000000-0000-4000-8000-000000000004",
];
const sellerId = "10000000-0000-4000-8000-000000000003";
const baseTime = new Date("2026-11-26T00:00:00Z").getTime();

db.Marketplace_Catalog.deleteMany({ _id: { $regex: "^f0010000-" } });

const docs = [];
for (let i = 1; i <= 1000; i++) {
  const pad = String(i).padStart(12, "0");
  const catIdx = i % categories.length;
  const rarIdx = Math.floor(i / categories.length) % rarities.length;
  const price = 800 + (i % 401);
  const ts = new Date(baseTime + i * 1000);

  docs.push({
    _id: `f0010000-0000-4000-8000-${pad}`,
    version: NumberLong(1),
    status: "Active",
    sellerId: sellerId,
    itemId: `f0020000-0000-4000-8000-${pad}`,
    templateId: templateIds[catIdx],
    itemName: `${rarities[rarIdx]} ${categories[catIdx]} #${i}`,
    category: categories[catIdx],
    rarity: rarities[rarIdx],
    quantity: NumberInt(1),
    unitPrice: NumberLong(price),
    grossGem: NumberLong(price),
    createdAt: ts,
    updatedAt: ts,
  });
}

db.Marketplace_Catalog.insertMany(docs);
print(
  "Seeded PF-01 listings:",
  db.Marketplace_Catalog.countDocuments({ _id: { $regex: "^f0010000-" } }),
);
