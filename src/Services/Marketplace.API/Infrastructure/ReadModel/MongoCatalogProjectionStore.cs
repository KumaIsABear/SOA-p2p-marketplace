using System.Text.RegularExpressions;
using Marketplace.API.Application.DTOs;
using Marketplace.API.Application.Interfaces;
using Marketplace.API.Application.Options;
using Marketplace.Contracts.V1;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Marketplace.API.Infrastructure.ReadModel;

public sealed class MongoCatalogProjectionStore : ICatalogProjectionStore
{
    private static readonly string[] TerminalStatuses =
    [
        nameof(ListingStatus.Sold),
        nameof(ListingStatus.Cancelled)
    ];

    private readonly IMongoCollection<CatalogListingDocument> _collection;
    private readonly ILogger<MongoCatalogProjectionStore> _logger;

    public MongoCatalogProjectionStore(
        IMongoDatabase database,
        IOptions<MongoDbOptions> options,
        ILogger<MongoCatalogProjectionStore> logger)
    {
        _collection = database.GetCollection<CatalogListingDocument>(options.Value.CatalogCollectionName);
        _logger = logger;
    }

    public async Task EnsureIndexesAsync(CancellationToken ct)
    {
        var keys = Builders<CatalogListingDocument>.IndexKeys;
        var models = new List<CreateIndexModel<CatalogListingDocument>>
        {
            new(keys.Ascending(x => x.Status).Ascending(x => x.UnitPrice).Ascending(x => x.Id)),
            new(keys.Ascending(x => x.Status).Descending(x => x.CreatedAt).Descending(x => x.Id)),
            new(keys.Ascending(x => x.Status).Ascending(x => x.Category).Ascending(x => x.Rarity)),
            new(keys.Ascending(x => x.SellerId).Ascending(x => x.Status))
        };

        await _collection.Indexes.CreateManyAsync(models, ct);
    }

    public async Task<bool> UpsertAsync(ListingSnapshot snapshot, CancellationToken ct)
    {
        var id = snapshot.ListingId.ToString("D");

        var filter = Builders<CatalogListingDocument>.Filter.And(
            Builders<CatalogListingDocument>.Filter.Eq(x => x.Id, id),
            Builders<CatalogListingDocument>.Filter.Lt(x => x.Version, snapshot.Version),
            Builders<CatalogListingDocument>.Filter.Nin(x => x.Status, TerminalStatuses)
        );

        var update = Builders<CatalogListingDocument>.Update
            .SetOnInsert(x => x.Id, id)
            .Set(x => x.Version, snapshot.Version)
            .Set(x => x.Status, snapshot.Status.ToString())
            .Set(x => x.SellerId, snapshot.SellerId.ToString("D"))
            .Set(x => x.ItemId, snapshot.ItemId.ToString("D"))
            .Set(x => x.TemplateId, snapshot.TemplateId.ToString("D"))
            .Set(x => x.ItemName, snapshot.ItemName)
            .Set(x => x.Category, snapshot.Category)
            .Set(x => x.Rarity, snapshot.Rarity)
            .Set(x => x.Quantity, snapshot.Quantity)
            .Set(x => x.UnitPrice, snapshot.UnitPrice)
            .Set(x => x.GrossGem, snapshot.GrossGem)
            .Set(x => x.CreatedAt, snapshot.CreatedAt.UtcDateTime)
            .Set(x => x.UpdatedAt, snapshot.UpdatedAt.UtcDateTime);

        try
        {
            var result = await _collection.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = true },
                ct
            );

            return result.ModifiedCount > 0 || result.UpsertedId is not null;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            _logger.LogDebug(
                "Ignored stale or post-tombstone projection event for listing {ListingId} (version {Version}, status {Status}).",
                snapshot.ListingId,
                snapshot.Version,
                snapshot.Status
            );
            return false;
        }
    }

    public async Task<ListingPageDto> SearchActiveAsync(CatalogSearchQuery query, CancellationToken ct)
    {
        var fb = Builders<CatalogListingDocument>.Filter;
        var filters = new List<FilterDefinition<CatalogListingDocument>>
        {
            fb.Eq(x => x.Status, nameof(ListingStatus.Active))
        };

        if (!string.IsNullOrEmpty(query.Query))
        {
            var escaped = Regex.Escape(query.Query);
            filters.Add(fb.Regex(x => x.ItemName, new BsonRegularExpression(escaped, "i")));
        }

        if (!string.IsNullOrEmpty(query.Category))
            filters.Add(fb.Eq(x => x.Category, query.Category));

        if (!string.IsNullOrEmpty(query.Rarity))
            filters.Add(fb.Eq(x => x.Rarity, query.Rarity));

        if (query.MinPrice.HasValue)
            filters.Add(fb.Gte(x => x.UnitPrice, query.MinPrice.Value));

        if (query.MaxPrice.HasValue)
            filters.Add(fb.Lte(x => x.UnitPrice, query.MaxPrice.Value));

        var combinedFilter = fb.And(filters);

        var sb = Builders<CatalogListingDocument>.Sort;
        var sortDef = query.Sort switch
        {
            "priceAsc" => sb.Ascending(x => x.UnitPrice).Ascending(x => x.Id),
            "priceDesc" => sb.Descending(x => x.UnitPrice).Descending(x => x.Id),
            _ => sb.Descending(x => x.CreatedAt).Descending(x => x.Id)
        };

        var totalCount = await _collection.CountDocumentsAsync(combinedFilter, cancellationToken: ct);

        var docs = await _collection
            .Find(combinedFilter)
            .Sort(sortDef)
            .Skip((query.Page - 1) * query.PageSize)
            .Limit(query.PageSize)
            .ToListAsync(ct);

        var items = docs.Select(d => d.ToDto()).ToList();
        return new ListingPageDto(items, query.Page, query.PageSize, totalCount);
    }
}
