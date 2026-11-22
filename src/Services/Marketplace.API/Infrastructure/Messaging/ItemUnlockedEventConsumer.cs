using Marketplace.API.Infrastructure.Persistence;
using Marketplace.Contracts.V1;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.API.Infrastructure.Messaging;

public sealed class ItemUnlockedEventConsumer : IConsumer<ItemUnlockedEvent>
{
    private readonly MarketplaceDbContext _db;

    public ItemUnlockedEventConsumer(MarketplaceDbContext db)
    {
        _db = db;
    }

    public async Task Consume(ConsumeContext<ItemUnlockedEvent> context)
    {
        var listing = await _db.Listings.SingleOrDefaultAsync(
            x => x.ListingId == context.Message.ListingId,
            context.CancellationToken
        );

        if (listing is null || listing.InventoryUnlockedAt.HasValue)
            return;

        listing.InventoryUnlockedAt = context.Message.OccurredAt;
        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
