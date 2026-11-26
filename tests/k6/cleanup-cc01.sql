SET NOCOUNT ON;

DELETE FROM AuthDb.dbo.Users
WHERE Email LIKE 'cc01.buyer.%@marketplace.local';

PRINT 'Removed CC-01 test buyer accounts from AuthDb.';
