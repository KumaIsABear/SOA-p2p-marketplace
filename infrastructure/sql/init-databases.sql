IF DB_ID(N'AuthDb') IS NULL
    CREATE DATABASE [AuthDb];
GO
IF DB_ID(N'WalletDb') IS NULL
    CREATE DATABASE [WalletDb];
GO
IF DB_ID(N'InventoryDb') IS NULL
    CREATE DATABASE [InventoryDb];
GO
IF DB_ID(N'MarketplaceWriteDb') IS NULL
    CREATE DATABASE [MarketplaceWriteDb];
GO
IF DB_ID(N'AdminAnalyticsDb') IS NULL
    CREATE DATABASE [AdminAnalyticsDb];
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'auth_runtime')
    CREATE LOGIN [auth_runtime] WITH PASSWORD = N'$(AUTH_DB_PASSWORD)', DEFAULT_DATABASE = [AuthDb], CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
ELSE
    ALTER LOGIN [auth_runtime] WITH PASSWORD = N'$(AUTH_DB_PASSWORD)';
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'wallet_runtime')
    CREATE LOGIN [wallet_runtime] WITH PASSWORD = N'$(WALLET_DB_PASSWORD)', DEFAULT_DATABASE = [WalletDb], CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
ELSE
    ALTER LOGIN [wallet_runtime] WITH PASSWORD = N'$(WALLET_DB_PASSWORD)';
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'inventory_runtime')
    CREATE LOGIN [inventory_runtime] WITH PASSWORD = N'$(INVENTORY_DB_PASSWORD)', DEFAULT_DATABASE = [InventoryDb], CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
ELSE
    ALTER LOGIN [inventory_runtime] WITH PASSWORD = N'$(INVENTORY_DB_PASSWORD)';
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'marketplace_runtime')
    CREATE LOGIN [marketplace_runtime] WITH PASSWORD = N'$(MARKETPLACE_DB_PASSWORD)', DEFAULT_DATABASE = [MarketplaceWriteDb], CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
ELSE
    ALTER LOGIN [marketplace_runtime] WITH PASSWORD = N'$(MARKETPLACE_DB_PASSWORD)';
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'admin_runtime')
    CREATE LOGIN [admin_runtime] WITH PASSWORD = N'$(ADMIN_DB_PASSWORD)', DEFAULT_DATABASE = [AdminAnalyticsDb], CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
ELSE
    ALTER LOGIN [admin_runtime] WITH PASSWORD = N'$(ADMIN_DB_PASSWORD)';
GO

USE [AuthDb];
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'auth_runtime')
    CREATE USER [auth_runtime] FOR LOGIN [auth_runtime];
ALTER ROLE [db_owner] ADD MEMBER [auth_runtime];
GO

USE [WalletDb];
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'wallet_runtime')
    CREATE USER [wallet_runtime] FOR LOGIN [wallet_runtime];
ALTER ROLE [db_owner] ADD MEMBER [wallet_runtime];
GO

USE [InventoryDb];
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'inventory_runtime')
    CREATE USER [inventory_runtime] FOR LOGIN [inventory_runtime];
ALTER ROLE [db_owner] ADD MEMBER [inventory_runtime];
GO

USE [MarketplaceWriteDb];
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'marketplace_runtime')
    CREATE USER [marketplace_runtime] FOR LOGIN [marketplace_runtime];
ALTER ROLE [db_owner] ADD MEMBER [marketplace_runtime];
GO

USE [AdminAnalyticsDb];
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'admin_runtime')
    CREATE USER [admin_runtime] FOR LOGIN [admin_runtime];
ALTER ROLE [db_owner] ADD MEMBER [admin_runtime];
GO
