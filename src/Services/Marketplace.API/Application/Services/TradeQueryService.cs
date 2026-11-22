using Marketplace.API.Application.Common;
using Marketplace.API.Application.DTOs;
using Marketplace.API.Application.Interfaces;
using Marketplace.API.Domain.Entities;
using Marketplace.API.Domain.Enums;
using Marketplace.API.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.API.Application.Services;

public sealed class TradeQueryService : ITradeQueryService
{
    private static readonly HashSet<string> ValidSides = new(StringComparer.Ordinal)
    {
        "buy",
        "sell",
        "all"
    };

    private readonly MarketplaceDbContext _db;
    private readonly IAuthStatusClient _authClient;

    public TradeQueryService(MarketplaceDbContext db, IAuthStatusClient authClient)
    {
        _db = db;
        _authClient = authClient;
    }

    public async Task<TradePageDto> GetHistoryAsync(
        Guid userId,
        string? side,
        string? status,
        int page,
        int pageSize,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct)
    {
        var errors = new List<string>();

        var effectiveSide = string.IsNullOrWhiteSpace(side) ? "all" : side.Trim();
        if (!ValidSides.Contains(effectiveSide))
            errors.Add("side must be one of: buy, sell, all.");

        var parsedStatus = TradeStatus.Completed;
        if (!string.IsNullOrWhiteSpace(status) && !Enum.TryParse(status.Trim(), ignoreCase: false, out parsedStatus))
            errors.Add($"Invalid trade status '{status}'.");

        if (page < 1)
            errors.Add("page must be at least 1.");

        if (pageSize is < 1 or > 100)
            errors.Add("pageSize must be between 1 and 100.");

        if (from.HasValue && to.HasValue && from.Value > to.Value)
            errors.Add("from cannot be later than to.");

        if (errors.Count > 0)
            throw new MarketplaceApiException(400, MarketplaceErrorCodes.ValidationError, "Request validation failed.", errors.ToArray());

        await EnsureNotBannedAsync(userId, ct);

        IQueryable<TradeHistory> query = _db.TradeHistories.AsNoTracking();

        query = effectiveSide switch
        {
            "buy" => query.Where(x => x.BuyerId == userId),
            "sell" => query.Where(x => x.SellerId == userId),
            _ => query.Where(x => x.BuyerId == userId || x.SellerId == userId)
        };

        query = query.Where(x => x.Status == parsedStatus);

        if (from.HasValue)
            query = query.Where(x => x.CreatedAt >= from.Value.ToUniversalTime());

        if (to.HasValue)
            query = query.Where(x => x.CreatedAt < to.Value.ToUniversalTime());

        var totalCount = await query.LongCountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.TradeId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => ToDto(x))
            .ToListAsync(ct);

        return new TradePageDto(items, page, pageSize, totalCount);
    }

    public async Task<TradeDto> GetTradeAsync(Guid requesterId, Guid tradeId, CancellationToken ct)
    {
        await EnsureNotBannedAsync(requesterId, ct);

        var trade = await _db.TradeHistories
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TradeId == tradeId, ct);

        if (trade is null || (trade.BuyerId != requesterId && trade.SellerId != requesterId))
            throw new MarketplaceApiException(404, MarketplaceErrorCodes.TradeNotFound, "Trade was not found.");

        return ToDto(trade);
    }

    private async Task EnsureNotBannedAsync(Guid userId, CancellationToken ct)
    {
        var access = await _authClient.GetAccessStatusAsync(userId, ct);
        if (string.Equals(access.Status, "Banned", StringComparison.OrdinalIgnoreCase))
            throw new MarketplaceApiException(403, MarketplaceErrorCodes.AccountBanned, "Account is banned.");
    }

    private static TradeDto ToDto(TradeHistory t) => new(
        t.TradeId,
        t.ListingId,
        t.BuyerId,
        t.SellerId,
        t.ItemId,
        t.TemplateId,
        t.Quantity,
        t.UnitPrice,
        t.GrossGem,
        t.FeeGem,
        t.SellerNetGem,
        t.Status,
        t.FailureCode,
        t.RequiresReview,
        t.CreatedAt,
        t.UpdatedAt,
        t.CompletedAt
    );
}
