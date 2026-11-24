using Marketplace.API.Application.Common;
using Marketplace.API.Application.DTOs;
using Marketplace.API.Application.Interfaces;
using Marketplace.API.Application.Options;
using Marketplace.API.Application.Services;
using Marketplace.API.Domain.Entities;
using Marketplace.API.Tests.Helpers;
using Marketplace.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Marketplace.API.Tests;

public sealed class CancelPurchaseRaceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CC04_CancelVsPurchaseRace_Across10FreshListings_ExactlyOneTransitionWins(bool useDistributedLock)
    {
        await using var dbFixture = new SqliteTestDatabase();
        await dbFixture.InitializeAsync();

        var sellerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var economy = Options.Create(new EconomyOptions { FeeBasisPoints = 500, ListingLockTtlSeconds = 20 });

        var auth = Substitute.For<IAuthStatusClient>();
        auth.GetAccessStatusAsync(sellerId, Arg.Any<CancellationToken>())
            .Returns(new AuthAccessStatusDto(sellerId, "Player", "Active", true, 1));
        auth.GetAccessStatusAsync(buyerId, Arg.Any<CancellationToken>())
            .Returns(new AuthAccessStatusDto(buyerId, "Player", "Active", true, 1));

        IDistributedListingLock listingLock = useDistributedLock
            ? new InMemoryDistributedListingLock()
            : new NoOpDistributedListingLock();

        for (var i = 0; i < 10; i++)
        {
            var listingId = Guid.NewGuid();
            var bus = new RecordingMessageBus();

            await using (var seedCtx = dbFixture.CreateContext())
            {
                var now = DateTimeOffset.UtcNow;
                seedCtx.Listings.Add(new Listing
                {
                    ListingId = listingId,
                    SellerId = sellerId,
                    ItemId = Guid.NewGuid(),
                    TemplateId = Guid.NewGuid(),
                    ItemName = $"Race Blade #{i}",
                    Category = "Weapon",
                    Rarity = "Rare",
                    Quantity = 1,
                    UnitPrice = 1000,
                    GrossGem = 1000,
                    FeeGem = 50,
                    SellerNetGem = 950,
                    FeeBasisPoints = 500,
                    PolicyVersion = 1,
                    Status = ListingStatus.Active,
                    Version = 2,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                await seedCtx.SaveChangesAsync();
            }

            using var startGate = new ManualResetEventSlim(false);

            var cancelTask = Task.Run(async () =>
            {
                startGate.Wait();
                await using var ctx = dbFixture.CreateContext();
                var svc = new ListingCommandService(
                    ctx,
                    auth,
                    Substitute.For<IInventoryClient>(),
                    Substitute.For<IPricePolicyProvider>(),
                    listingLock,
                    bus.PublishEndpoint,
                    bus.SendEndpointProvider,
                    economy,
                    NullLogger<ListingCommandService>.Instance
                );

                try
                {
                    await svc.CancelListingAsync(sellerId, listingId, CancellationToken.None);
                    return (Succeeded: true, ErrorCode: (string?)null);
                }
                catch (MarketplaceApiException ex) when (ex.StatusCode == 409)
                {
                    return (Succeeded: false, ErrorCode: ex.Code);
                }
            });

            var purchaseTask = Task.Run(async () =>
            {
                startGate.Wait();
                await using var ctx = dbFixture.CreateContext();
                var svc = new PurchaseService(
                    ctx,
                    auth,
                    listingLock,
                    bus.SendEndpointProvider,
                    bus.PublishEndpoint,
                    economy
                );

                try
                {
                    await svc.PurchaseListingAsync(buyerId, listingId, $"cc04-key-{useDistributedLock}-{i}", CancellationToken.None);
                    return (Succeeded: true, ErrorCode: (string?)null);
                }
                catch (MarketplaceApiException ex) when (ex.StatusCode == 409)
                {
                    return (Succeeded: false, ErrorCode: ex.Code);
                }
            });

            startGate.Set();
            var results = await Task.WhenAll(cancelTask, purchaseTask);
            var cancelResult = results[0];
            var purchaseResult = results[1];

            Assert.NotEqual(cancelResult.Succeeded, purchaseResult.Succeeded);

            await using var verifyCtx = dbFixture.CreateContext();
            var finalListing = await verifyCtx.Listings.AsNoTracking().SingleAsync(x => x.ListingId == listingId);
            var trades = await verifyCtx.TradeHistories.AsNoTracking().Where(x => x.ListingId == listingId).ToListAsync();

            if (cancelResult.Succeeded)
            {
                Assert.Equal(ListingStatus.Cancelled, finalListing.Status);
                Assert.Empty(trades);
                Assert.Single(bus.Sent, x => x.Message is UnlockItemCommand);
                Assert.DoesNotContain(bus.Sent, x => x.Message is HoldBuyerFundsCommand);
                Assert.Contains(purchaseResult.ErrorCode, new[] { MarketplaceErrorCodes.ListingBusy, MarketplaceErrorCodes.ListingNotActive });
            }
            else
            {
                Assert.Equal(ListingStatus.Processing, finalListing.Status);
                Assert.Single(trades);
                Assert.Single(bus.Sent, x => x.Message is HoldBuyerFundsCommand);
                Assert.DoesNotContain(bus.Sent, x => x.Message is UnlockItemCommand);
                Assert.Contains(cancelResult.ErrorCode, new[] { MarketplaceErrorCodes.ListingBusy, MarketplaceErrorCodes.ListingNotActive });
            }
        }
    }
}
