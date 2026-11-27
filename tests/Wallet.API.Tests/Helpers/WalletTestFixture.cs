using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wallet.API.Application.Services;
using Wallet.API.Infrastructure.Persistence;

namespace Wallet.API.Tests.Helpers;

public sealed class WalletTestFixture : IAsyncDisposable {
    private readonly SqliteConnection _connection;

    public WalletTestFixture() {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    public WalletDbContext CreateDbContext() {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new WalletDbContext(options);
    }

    public WalletSagaService CreateSagaService(WalletDbContext dbContext) {
        return new WalletSagaService(dbContext);
    }

    public TopUpSettlementService CreateTopUpSettlementService(WalletDbContext dbContext) {
        return new TopUpSettlementService(dbContext, NullLogger<TopUpSettlementService>.Instance);
    }

    public async ValueTask DisposeAsync() {
        await _connection.DisposeAsync();
    }
}
