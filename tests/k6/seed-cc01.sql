SET NOCOUNT ON;

DECLARE @PasswordHash NVARCHAR(256);
SELECT @PasswordHash = PasswordHash
FROM AuthDb.dbo.Users
WHERE UserId = '10000000-0000-4000-8000-000000000004';

IF @PasswordHash IS NULL
BEGIN
    RAISERROR('Seed buyer account not found in AuthDb.', 16, 1);
    RETURN;
END;

DECLARE @i INT = 1;
WHILE @i <= 100
BEGIN
    DECLARE @Num CHAR(3) = RIGHT('000' + CAST(@i AS VARCHAR(3)), 3);
    DECLARE @UserId UNIQUEIDENTIFIER = CAST(CONCAT('c0010000-0000-4000-8000-', RIGHT('000000000000' + CAST(@i AS VARCHAR(12)), 12)) AS UNIQUEIDENTIFIER);
    DECLARE @Email NVARCHAR(256) = CONCAT('cc01.buyer.', @Num, '@marketplace.local');

    IF NOT EXISTS (SELECT 1 FROM AuthDb.dbo.Users WHERE UserId = @UserId)
    BEGIN
        INSERT INTO AuthDb.dbo.Users (UserId, Email, NormalizedEmail, DisplayName, PasswordHash, Role, Status, Version)
        VALUES (@UserId, @Email, UPPER(@Email), CONCAT('CC01 Buyer ', @Num), @PasswordHash, 'Player', 'Active', 1);
    END;

    IF NOT EXISTS (SELECT 1 FROM WalletDb.dbo.Wallets WHERE UserId = @UserId)
    BEGIN
        INSERT INTO WalletDb.dbo.Wallets (UserId, TotalBalance, HoldBalance, Version)
        VALUES (@UserId, 10000, 0, 1);
    END
    ELSE
    BEGIN
        UPDATE WalletDb.dbo.Wallets
        SET TotalBalance = 10000, HoldBalance = 0
        WHERE UserId = @UserId;
    END;

    IF NOT EXISTS (SELECT 1 FROM InventoryDb.dbo.Inventories WHERE UserId = @UserId)
    BEGIN
        INSERT INTO InventoryDb.dbo.Inventories (UserId, Capacity, ReservedSlots, Version)
        VALUES (@UserId, 20, 0, 1);
    END;

    SET @i = @i + 1;
END;

PRINT 'Seeded 100 CC-01 buyers across AuthDb, WalletDb, and InventoryDb.';
