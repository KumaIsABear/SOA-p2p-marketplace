const res = db.Marketplace_Catalog.deleteMany({
  _id: { $regex: "^f0010000-" },
});
print("Removed PF-01 listings:", res.deletedCount);
