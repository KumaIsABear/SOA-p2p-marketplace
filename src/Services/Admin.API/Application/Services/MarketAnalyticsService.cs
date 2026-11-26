using Admin.API.Application.Common;
using Admin.API.Application.DTOs;
using Admin.API.Application.Interfaces;
using Admin.API.Application.Options;
using Admin.API.Domain.Entities;
using Admin.API.Infrastructure.Persistence;
using Marketplace.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Admin.API.Application.Services;

public sealed class MarketAnalyticsService : IMarketAnalyticsService {
    private const int MaxHistoryRangeDays = 31;

    private readonly AdminDbContext _db;
    private readonly IAnalyticsCache _analyticsCache;
    private readonly IPricePolicyService _pricePolicyService;
    private readonly IWalletInternalClient _walletClient;
    private readonly IMarketplaceInternalClient _marketplaceClient;
    private readonly TimeZoneInfo _businessTimeZone;
    private readonly ILogger<MarketAnalyticsService> _logger;

    public MarketAnalyticsService(
        AdminDbContext db,
        IAnalyticsCache analyticsCache,
        IPricePolicyService pricePolicyService,
        IWalletInternalClient walletClient,
        IMarketplaceInternalClient marketplaceClient,
        IOptions<AnalyticsOptions> analyticsOptions,
        ILogger<MarketAnalyticsService> logger
    ) {
        _db = db;
        _analyticsCache = analyticsCache;
        _pricePolicyService = pricePolicyService;
        _walletClient = walletClient;
        _marketplaceClient = marketplaceClient;
        _businessTimeZone = ResolveTimeZone(analyticsOptions.Value.TimeZoneId);
        _logger = logger;
    }

    public DateOnly GetCurrentBusinessDay() {
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _businessTimeZone);
        return DateOnly.FromDateTime(localNow.Date);
    }

    public async Task RecordTradeCompletedAsync(TradeCompletedEvent tradeEvent, CancellationToken cancellationToken) {
        if (tradeEvent.TradeId == Guid.Empty || tradeEvent.TemplateId == Guid.Empty || tradeEvent.Quantity < 1 || tradeEvent.GrossGem < 1 || tradeEvent.FeeGem < 0) {
            _logger.LogWarning("Skipping invalid TradeCompletedEvent TradeId={TradeId}.", tradeEvent.TradeId);
            return;
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () => {
            _db.ChangeTracker.Clear();

            var exists = await _db.TradeFacts
                .AsNoTracking()
                .AnyAsync(tf => tf.TradeId == tradeEvent.TradeId, cancellationToken);

            if (exists) {
                return;
            }

            _db.TradeFacts.Add(new TradeFact {
                TradeId = tradeEvent.TradeId,
                TemplateId = tradeEvent.TemplateId,
                GrossGem = tradeEvent.GrossGem,
                FeeGem = tradeEvent.FeeGem,
                Quantity = tradeEvent.Quantity,
                CompletedAt = tradeEvent.CompletedAt.ToUniversalTime()
            });

            try {
                await _db.SaveChangesAsync(cancellationToken);
            } catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex)) {
                _db.ChangeTracker.Clear();
            }
        });

        var eventDay = ToBusinessDay(tradeEvent.CompletedAt);
        var today = GetCurrentBusinessDay();
        var now = DateTimeOffset.UtcNow;

        var computed = await ComputeTemplateDayStatisticFromFactsAsync(eventDay, tradeEvent.TemplateId, now, cancellationToken);
        if (computed is not null) {
            await TrySetCacheAsync(computed, cancellationToken);

            if (eventDay < today) {
                await UpsertDailySummaryAsync(computed, now, cancellationToken);
            }
        }

        await _pricePolicyService.GetAndRefreshAsync(tradeEvent.TemplateId, cancellationToken);
    }

    public async Task ReconcileDayAsync(DateOnly businessDay, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var stats = await ComputeDayStatisticsFromFactsAsync(businessDay, null, now, cancellationToken);

        foreach (var stat in stats) {
            await UpsertDailySummaryAsync(stat, now, cancellationToken);
            await TrySetCacheAsync(stat, cancellationToken);
        }

        _logger.LogInformation("Reconciled market daily summaries for business day {Day}: {Count} templates.", businessDay.ToString("yyyy-MM-dd"), stats.Count);
    }

    public async Task<AdminResult<MarketOverviewDto>> GetOverviewAsync(
        DateOnly? day,
        string correlationId,
        CancellationToken cancellationToken
    ) {
        var today = GetCurrentBusinessDay();
        var targetDay = day ?? today;
        var now = DateTimeOffset.UtcNow;

        var dayStats = await LoadDayStatisticsAsync(targetDay, null, today, now, cancellationToken);

        var tradeCount = dayStats.Sum(s => s.TradeCount);
        var volume = dayStats.Sum(s => s.Volume);
        var totalGrossGem = dayStats.Sum(s => s.TotalGrossGem);
        var totalFeeGem = dayStats.Sum(s => s.TotalFeeGem);

        var supplyTask = _walletClient.GetSupplyAsync(correlationId, cancellationToken);
        var listingStatsTask = _marketplaceClient.GetStatsAsync(correlationId, cancellationToken);

        await Task.WhenAll(supplyTask, listingStatsTask);

        var supplyResult = supplyTask.Result;
        var listingStatsResult = listingStatsTask.Result;

        var unavailableSources = new List<string>();
        WalletSupplyDto? gemSupply = null;
        long? activeListingCount = null;

        if (supplyResult.Outcome == InternalCallOutcome.Success && supplyResult.Value is not null) {
            gemSupply = supplyResult.Value;
        } else {
            unavailableSources.Add("wallet");
        }

        if (listingStatsResult.Outcome == InternalCallOutcome.Success && listingStatsResult.Value is not null) {
            activeListingCount = listingStatsResult.Value.ActiveListingCount;
        } else {
            unavailableSources.Add("marketplace");
        }

        var overview = new MarketOverviewDto(
            targetDay,
            tradeCount,
            volume,
            totalGrossGem,
            totalFeeGem,
            gemSupply,
            activeListingCount,
            unavailableSources.Count > 0,
            unavailableSources,
            now
        );

        return AdminResult<MarketOverviewDto>.Success(overview);
    }

    public async Task<AdminResult<MarketStatisticPageDto>> GetHistoryAsync(
        Guid? templateId,
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken
    ) {
        var errors = new List<string>();

        if (templateId.HasValue && templateId.Value == Guid.Empty) {
            errors.Add("templateId must be a valid non-empty UUID.");
        }

        if (from is null) {
            errors.Add("from query parameter is required (format YYYY-MM-DD).");
        }

        if (to is null) {
            errors.Add("to query parameter is required (format YYYY-MM-DD).");
        }

        if (from.HasValue && to.HasValue) {
            if (from.Value >= to.Value) {
                errors.Add("from must be strictly earlier than to (half-open range [from, to)).");
            } else if (to.Value.DayNumber - from.Value.DayNumber > MaxHistoryRangeDays) {
                errors.Add($"Date range cannot exceed {MaxHistoryRangeDays} days.");
            }
        }

        if (page < 1) {
            errors.Add("page must be greater than or equal to 1.");
        }

        if (pageSize < 1 || pageSize > 100) {
            errors.Add("pageSize must be between 1 and 100.");
        }

        if (errors.Count > 0) {
            return AdminResult<MarketStatisticPageDto>.Failure(
                AdminErrors.ValidationError("One or more validation errors occurred.", errors.ToArray())
            );
        }

        var fromDay = from!.Value;
        var toDay = to!.Value;
        var today = GetCurrentBusinessDay();
        var now = DateTimeOffset.UtcNow;

        var combined = new Dictionary<(DateOnly Day, Guid TemplateId), MarketStatisticDto>();

        var pastEnd = toDay < today ? toDay : today;
        if (fromDay < pastEnd) {
            var summariesQuery = _db.MarketDailySummaries
                .AsNoTracking()
                .Where(s => s.Day >= fromDay && s.Day < pastEnd);

            if (templateId.HasValue) {
                summariesQuery = summariesQuery.Where(s => s.TemplateId == templateId.Value);
            }

            var summaries = await summariesQuery.ToListAsync(cancellationToken);
            foreach (var s in summaries) {
                combined[(s.Day, s.TemplateId)] = new MarketStatisticDto(
                    s.Day,
                    s.TemplateId,
                    s.TradeCount,
                    s.Volume,
                    s.TotalGrossGem,
                    s.TotalFeeGem,
                    s.MinUnitPrice,
                    s.MaxUnitPrice,
                    s.ReferencePrice,
                    s.UpdatedAt
                );
            }

            var (pastStartUtc, _) = GetBusinessDayUtcRange(fromDay);
            var (pastEndUtc, _) = GetBusinessDayUtcRange(pastEnd);

            var pastFactsQuery = _db.TradeFacts
                .AsNoTracking()
                .Where(tf => tf.CompletedAt >= pastStartUtc && tf.CompletedAt < pastEndUtc);

            if (templateId.HasValue) {
                pastFactsQuery = pastFactsQuery.Where(tf => tf.TemplateId == templateId.Value);
            }

            var pastFacts = await pastFactsQuery.ToListAsync(cancellationToken);
            foreach (var group in pastFacts.GroupBy(tf => (Day: ToBusinessDay(tf.CompletedAt), tf.TemplateId))) {
                if (!combined.ContainsKey(group.Key)) {
                    combined[group.Key] = BuildStatistic(group.Key.Day, group.Key.TemplateId, group, now);
                }
            }
        }

        if (fromDay <= today && today < toDay) {
            var todayStats = await LoadTodayStatisticsAsync(today, templateId, now, cancellationToken);
            foreach (var stat in todayStats) {
                combined[(stat.Day, stat.TemplateId)] = stat;
            }
        }

        var ordered = combined.Values
            .OrderByDescending(s => s.Day)
            .ThenBy(s => s.TemplateId)
            .ToList();

        var totalCount = (long)ordered.Count;
        var pageItems = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return AdminResult<MarketStatisticPageDto>.Success(
            new MarketStatisticPageDto(pageItems, page, pageSize, totalCount)
        );
    }

    private async Task<List<MarketStatisticDto>> LoadDayStatisticsAsync(
        DateOnly targetDay,
        Guid? templateId,
        DateOnly today,
        DateTimeOffset now,
        CancellationToken cancellationToken
    ) {
        if (targetDay == today) {
            return await LoadTodayStatisticsAsync(today, templateId, now, cancellationToken);
        }

        var query = _db.MarketDailySummaries
            .AsNoTracking()
            .Where(s => s.Day == targetDay);

        if (templateId.HasValue) {
            query = query.Where(s => s.TemplateId == templateId.Value);
        }

        var summaries = await query.ToListAsync(cancellationToken);
        if (summaries.Count > 0) {
            return summaries.Select(s => new MarketStatisticDto(
                s.Day,
                s.TemplateId,
                s.TradeCount,
                s.Volume,
                s.TotalGrossGem,
                s.TotalFeeGem,
                s.MinUnitPrice,
                s.MaxUnitPrice,
                s.ReferencePrice,
                s.UpdatedAt
            )).ToList();
        }

        return await ComputeDayStatisticsFromFactsAsync(targetDay, templateId, now, cancellationToken);
    }

    private async Task<List<MarketStatisticDto>> LoadTodayStatisticsAsync(
        DateOnly today,
        Guid? templateId,
        DateTimeOffset now,
        CancellationToken cancellationToken
    ) {
        if (templateId.HasValue) {
            var cached = await TryGetCacheAsync(today, templateId.Value, cancellationToken);
            if (cached is not null) {
                return [cached];
            }

            var computedSingle = await ComputeTemplateDayStatisticFromFactsAsync(today, templateId.Value, now, cancellationToken);
            if (computedSingle is not null) {
                await TrySetCacheAsync(computedSingle, cancellationToken);
                return [computedSingle];
            }

            return [];
        }

        var computed = await ComputeDayStatisticsFromFactsAsync(today, null, now, cancellationToken);
        foreach (var stat in computed) {
            await TrySetCacheAsync(stat, cancellationToken);
        }

        return computed;
    }

    private async Task<MarketStatisticDto?> ComputeTemplateDayStatisticFromFactsAsync(
        DateOnly businessDay,
        Guid templateId,
        DateTimeOffset now,
        CancellationToken cancellationToken
    ) {
        var list = await ComputeDayStatisticsFromFactsAsync(businessDay, templateId, now, cancellationToken);
        return list.Count > 0 ? list[0] : null;
    }

    private async Task<List<MarketStatisticDto>> ComputeDayStatisticsFromFactsAsync(
        DateOnly businessDay,
        Guid? templateId,
        DateTimeOffset now,
        CancellationToken cancellationToken
    ) {
        var (startUtc, endUtc) = GetBusinessDayUtcRange(businessDay);

        var query = _db.TradeFacts
            .AsNoTracking()
            .Where(tf => tf.CompletedAt >= startUtc && tf.CompletedAt < endUtc);

        if (templateId.HasValue) {
            query = query.Where(tf => tf.TemplateId == templateId.Value);
        }

        var facts = await query.ToListAsync(cancellationToken);
        if (facts.Count == 0) {
            return [];
        }

        return facts
            .GroupBy(tf => tf.TemplateId)
            .OrderBy(g => g.Key)
            .Select(g => BuildStatistic(businessDay, g.Key, g, now))
            .ToList();
    }

    private static MarketStatisticDto BuildStatistic(
        DateOnly day,
        Guid templateId,
        IEnumerable<TradeFact> facts,
        DateTimeOffset asOf
    ) {
        long tradeCount = 0;
        long volume = 0;
        long totalGross = 0;
        long totalFee = 0;
        long? minUnit = null;
        long? maxUnit = null;

        checked {
            foreach (var fact in facts) {
                tradeCount++;
                volume += fact.Quantity;
                totalGross += fact.GrossGem;
                totalFee += fact.FeeGem;

                var unitPrice = fact.GrossGem / fact.Quantity;
                if (minUnit is null || unitPrice < minUnit.Value) {
                    minUnit = unitPrice;
                }
                if (maxUnit is null || unitPrice > maxUnit.Value) {
                    maxUnit = unitPrice;
                }
            }
        }

        var referencePrice = volume > 0 ? Math.Max(1L, totalGross / volume) : 1L;

        return new MarketStatisticDto(
            day,
            templateId,
            tradeCount,
            volume,
            totalGross,
            totalFee,
            minUnit,
            maxUnit,
            referencePrice,
            asOf
        );
    }

    private async Task UpsertDailySummaryAsync(MarketStatisticDto stat, DateTimeOffset now, CancellationToken cancellationToken) {
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () => {
            _db.ChangeTracker.Clear();

            var existing = await _db.MarketDailySummaries
                .FirstOrDefaultAsync(s => s.Day == stat.Day && s.TemplateId == stat.TemplateId, cancellationToken);

            if (existing is null) {
                _db.MarketDailySummaries.Add(new MarketDailySummary {
                    Day = stat.Day,
                    TemplateId = stat.TemplateId,
                    TradeCount = stat.TradeCount,
                    TotalGrossGem = stat.TotalGrossGem,
                    TotalFeeGem = stat.TotalFeeGem,
                    Volume = stat.Volume,
                    MinUnitPrice = stat.MinUnitPrice,
                    MaxUnitPrice = stat.MaxUnitPrice,
                    ReferencePrice = stat.ReferencePrice,
                    UpdatedAt = now
                });
            } else {
                existing.TradeCount = stat.TradeCount;
                existing.TotalGrossGem = stat.TotalGrossGem;
                existing.TotalFeeGem = stat.TotalFeeGem;
                existing.Volume = stat.Volume;
                existing.MinUnitPrice = stat.MinUnitPrice;
                existing.MaxUnitPrice = stat.MaxUnitPrice;
                existing.ReferencePrice = stat.ReferencePrice;
                existing.UpdatedAt = now;
            }

            try {
                await _db.SaveChangesAsync(cancellationToken);
            } catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex)) {
                _db.ChangeTracker.Clear();
                var raced = await _db.MarketDailySummaries
                    .FirstAsync(s => s.Day == stat.Day && s.TemplateId == stat.TemplateId, cancellationToken);

                raced.TradeCount = stat.TradeCount;
                raced.TotalGrossGem = stat.TotalGrossGem;
                raced.TotalFeeGem = stat.TotalFeeGem;
                raced.Volume = stat.Volume;
                raced.MinUnitPrice = stat.MinUnitPrice;
                raced.MaxUnitPrice = stat.MaxUnitPrice;
                raced.ReferencePrice = stat.ReferencePrice;
                raced.UpdatedAt = now;

                await _db.SaveChangesAsync(cancellationToken);
            }
        });
    }

    private async Task<MarketStatisticDto?> TryGetCacheAsync(DateOnly day, Guid templateId, CancellationToken cancellationToken) {
        try {
            return await _analyticsCache.GetAsync(day, templateId, cancellationToken);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Redis analytics read failed for {Day}/{TemplateId}.", day, templateId);
            return null;
        }
    }

    private async Task TrySetCacheAsync(MarketStatisticDto stat, CancellationToken cancellationToken) {
        try {
            await _analyticsCache.SetAsync(stat, cancellationToken);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Redis analytics write failed for {Day}/{TemplateId}.", stat.Day, stat.TemplateId);
        }
    }

    private DateOnly ToBusinessDay(DateTimeOffset timestampUtc) {
        var local = TimeZoneInfo.ConvertTime(timestampUtc, _businessTimeZone);
        return DateOnly.FromDateTime(local.Date);
    }

    private (DateTimeOffset StartUtc, DateTimeOffset EndUtc) GetBusinessDayUtcRange(DateOnly businessDay) {
        var localStart = new DateTime(businessDay.Year, businessDay.Month, businessDay.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var localEnd = localStart.AddDays(1);

        var startUtc = new DateTimeOffset(localStart, _businessTimeZone.GetUtcOffset(localStart)).ToUniversalTime();
        var endUtc = new DateTimeOffset(localEnd, _businessTimeZone.GetUtcOffset(localEnd)).ToUniversalTime();
        return (startUtc, endUtc);
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId) {
        try {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        } catch (TimeZoneNotFoundException) {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) {
        return ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627);
    }
}
