const dbName = 'MarketplaceReadDb';
const username = 'marketplace_runtime';
const password = process.env.MONGO_MARKETPLACE_PASSWORD;

if (!password) {
  throw new Error('MONGO_MARKETPLACE_PASSWORD environment variable is required.');
}

const readDb = db.getSiblingDB(dbName);
const existingUser = readDb.getUser(username);

if (!existingUser) {
  readDb.createUser({
    user: username,
    pwd: password,
    roles: [{ role: 'readWrite', db: dbName }]
  });
}
