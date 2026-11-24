using System.Collections.Concurrent;
using Marketplace.API.Application.DTOs;
using Marketplace.API.Application.Interfaces;
using Marketplace.API.Infrastructure.Persistence;
using Marketplace.Contracts.V1;
using MassTransit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NSubstitute;

namespace Marketplace.API.Tests.Helpers;

public sealed class TestMarketplaceDbContext : MarketplaceDbContext
{
    public TestMarketplaceDbContext(DbContextOptions<MarketplaceDbContext> options) : base(options) { }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }
}

public sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly string _dbPath;
    public string ConnectionString { get; }

    public SqliteTestDatabase()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"mkt-test-{Guid.NewGuid():N}.db");
        ConnectionString = $"Data Source={_dbPath};Default Timeout=5";
    }

    public TestMarketplaceDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MarketplaceDbContext>()
            .UseSqlite(ConnectionString)
            .Options;

        return new TestMarketplaceDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        await ctx.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;");
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        if (File.Exists(_dbPath + "-wal"))
            File.Delete(_dbPath + "-wal");
        if (File.Exists(_dbPath + "-shm"))
            File.Delete(_dbPath + "-shm");
        return ValueTask.CompletedTask;
    }
}

public sealed class InMemoryDistributedListingLock : IDistributedListingLock
{
    private readonly ConcurrentDictionary<Guid, byte> _held = new();

    public Task<IAsyncDisposable?> TryAcquireAsync(Guid listingId, TimeSpan ttl, CancellationToken ct)
    {
        if (_held.TryAdd(listingId, 0))
            return Task.FromResult<IAsyncDisposable?>(new Releaser(() => _held.TryRemove(listingId, out _)));

        return Task.FromResult<IAsyncDisposable?>(null);
    }

    private sealed class Releaser(Action onDispose) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            onDispose();
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class NoOpDistributedListingLock : IDistributedListingLock
{
    public Task<IAsyncDisposable?> TryAcquireAsync(Guid listingId, TimeSpan ttl, CancellationToken ct) =>
        Task.FromResult<IAsyncDisposable?>(new DummyReleaser());

    private sealed class DummyReleaser : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public sealed class RecordingMessageBus
{
    public ConcurrentBag<object> Published { get; } = [];
    public ConcurrentBag<(Uri Destination, object Message)> Sent { get; } = [];

    public IPublishEndpoint PublishEndpoint { get; }
    public ISendEndpointProvider SendEndpointProvider { get; }

    public RecordingMessageBus()
    {
        PublishEndpoint = Substitute.For<IPublishEndpoint>();

        PublishEndpoint
            .Publish(Arg.Any<ListingCreatedEvent>(), Arg.Any<CancellationToken>())
            .Returns(ci => { Published.Add(ci.Arg<ListingCreatedEvent>()); return Task.CompletedTask; });
        PublishEndpoint
            .Publish(Arg.Any<ListingProcessingEvent>(), Arg.Any<CancellationToken>())
            .Returns(ci => { Published.Add(ci.Arg<ListingProcessingEvent>()); return Task.CompletedTask; });
        PublishEndpoint
            .Publish(Arg.Any<ListingCancelledEvent>(), Arg.Any<CancellationToken>())
            .Returns(ci => { Published.Add(ci.Arg<ListingCancelledEvent>()); return Task.CompletedTask; });
        PublishEndpoint
            .Publish(Arg.Any<ListingReactivatedEvent>(), Arg.Any<CancellationToken>())
            .Returns(ci => { Published.Add(ci.Arg<ListingReactivatedEvent>()); return Task.CompletedTask; });
        PublishEndpoint
            .Publish(Arg.Any<TradeCompletedEvent>(), Arg.Any<CancellationToken>())
            .Returns(ci => { Published.Add(ci.Arg<TradeCompletedEvent>()); return Task.CompletedTask; });
        PublishEndpoint
            .Publish(Arg.Any<TradeFailedEvent>(), Arg.Any<CancellationToken>())
            .Returns(ci => { Published.Add(ci.Arg<TradeFailedEvent>()); return Task.CompletedTask; });

        SendEndpointProvider = Substitute.For<ISendEndpointProvider>();
        SendEndpointProvider
            .GetSendEndpoint(Arg.Any<Uri>())
            .Returns(ci => Task.FromResult(CreateSendEndpoint(ci.Arg<Uri>())));
    }

    private ISendEndpoint CreateSendEndpoint(Uri destination)
    {
        var ep = Substitute.For<ISendEndpoint>();

        ep.Send(Arg.Any<HoldBuyerFundsCommand>(), Arg.Any<CancellationToken>())
            .Returns(ci => { Sent.Add((destination, ci.Arg<HoldBuyerFundsCommand>())); return Task.CompletedTask; });
        ep.Send(Arg.Any<TransferItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(ci => { Sent.Add((destination, ci.Arg<TransferItemCommand>())); return Task.CompletedTask; });
        ep.Send(Arg.Any<SettleTradeCommand>(), Arg.Any<CancellationToken>())
            .Returns(ci => { Sent.Add((destination, ci.Arg<SettleTradeCommand>())); return Task.CompletedTask; });
        ep.Send(Arg.Any<FinalizeItemTransferCommand>(), Arg.Any<CancellationToken>())
            .Returns(ci => { Sent.Add((destination, ci.Arg<FinalizeItemTransferCommand>())); return Task.CompletedTask; });
        ep.Send(Arg.Any<ReverseItemTransferCommand>(), Arg.Any<CancellationToken>())
            .Returns(ci => { Sent.Add((destination, ci.Arg<ReverseItemTransferCommand>())); return Task.CompletedTask; });
        ep.Send(Arg.Any<RefundBuyerFundsCommand>(), Arg.Any<CancellationToken>())
            .Returns(ci => { Sent.Add((destination, ci.Arg<RefundBuyerFundsCommand>())); return Task.CompletedTask; });
        ep.Send(Arg.Any<UnlockItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(ci => { Sent.Add((destination, ci.Arg<UnlockItemCommand>())); return Task.CompletedTask; });

        return ep;
    }
}
