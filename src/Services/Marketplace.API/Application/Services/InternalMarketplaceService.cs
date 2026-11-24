using Marketplace.API.Application.Common;
using Marketplace.API.Application.DTOs;
using Marketplace.API.Application.Interfaces;
using Marketplace.API.Domain.Entities;
using Marketplace.API.Domain.Enums;
using Marketplace.API.Infrastructure.Persistence;
using Marketplace.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.API.Application.Services;

public sealed class InternalMarketplaceService : IInternalMarketplaceService
{
    private readonly MarketplaceDbContext _db;

    public InternalMarketplaceService(MarketplaceDbContext db)
    {
        _db = db;
    }

    public async Task<MarketTradeTraceDto> GetTradeTraceAsync(Guid tradeId, CancellationToken ct)
    {
        var trade = await _db.TradeHistories
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TradeId == tradeId, ct);

        if (trade is null)
            throw new MarketplaceApiException(404, MarketplaceErrorCodes.TradeNotFound, "Trade was not found.");

        var steps = await _db.SagaSteps
            .AsNoTracking()
            .Where(x => x.TradeId == tradeId)
            .OrderBy(x => x.At)
            .ThenBy(x => x.StepId)
            .Select(x => new SagaStepDto(x.Step, x.Outcome, x.At, x.ErrorCode))
            .ToListAsync(ct);

        return new MarketTradeTraceDto(ToDto(trade), trade.TradeId, steps);
    }

    public async Task<TradePageDto> SearchTradesByUserAsync(
        Guid userId,
        string? status,
        int page,
        int pageSize,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct)
    {
        var errors = new List<string>();

        if (userId == Guid.Empty)
            errors.Add("userId is required.");

        TradeStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (Enum.TryParse<TradeStatus>(status.Trim(), ignoreCase: false, out var s))
                parsedStatus = s;
            else
                errors.Add($"Invalid trade status '{status}'.");
        }

        if (page < 1)
            errors.Add("page must be at least 1.");

        if (pageSize is < 1 or > 100)
            errors.Add("pageSize must be between 1 and 100.");

        if (from.HasValue && to.HasValue && from.Value > to.Value)
            errors.Add("from cannot be later than to.");

        if (errors.Count > 0)
            throw new MarketplaceApiException(400, MarketplaceErrorCodes.ValidationError, "Request validation failed.", errors.ToArray());

        IQueryable<TradeHistory> query = _db.TradeHistories
            .AsNoTracking()
            .Where(x => x.BuyerId == userId || x.SellerId == userId);

        if (parsedStatus.HasValue)
            query = query.Where(x => x.Status == parsedStatus.Value);

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

    public async Task<MarketListingStatsDto> GetStatsAsync(CancellationToken ct)
    {
        var count = await _db.Listings
            .AsNoTracking()
            .LongCountAsync(x => x.Status == ListingStatus.Active, ct);

        return new MarketListingStatsDto(count, DateTimeOffset.UtcNow);
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
