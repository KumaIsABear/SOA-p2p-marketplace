using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

public sealed class PricePolicyService : IPricePolicyService {
    private const string UpsertOperationName = "ADM-04:UpsertPricePolicy";
    private const int MaxConcurrencyRetries = 3;

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AdminDbContext _db;
    private readonly IPricePolicyCache _cache;
    private readonly TimeZoneInfo _businessTimeZone;
    private readonly ILogger<PricePolicyService> _logger;

    public PricePolicyService(
        AdminDbContext db,
        IPricePolicyCache cache,
        IOptions<AnalyticsOptions> analyticsOptions,
        ILogger<PricePolicyService> logger
    ) {
        _db = db;
        _cache = cache;
        _businessTimeZone = ResolveTimeZone(analyticsOptions.Value.TimeZoneId);
        _logger = logger;
    }

    public async Task<AdminResult<PricePolicyPageDto>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) {
        var validationErrors = new List<string>();
        if (page < 1) {
            validationErrors.Add("page must be greater than or equal to 1.");
        }
        if (pageSize < 1 || pageSize > 100) {
            validationErrors.Add("pageSize must be between 1 and 100.");
        }

        if (validationErrors.Count > 0) {
            return AdminResult<PricePolicyPageDto>.Failure(
                AdminErrors.ValidationError("One or more validation errors occurred.", validationErrors.ToArray())
            );
        }

        var totalCount = await _db.PricePolicies.LongCountAsync(cancellationToken);
        var policies = await _db.PricePolicies
            .AsNoTracking()
            .OrderBy(p => p.TemplateId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var referencePrices = await ResolveReferencePricesAsync(policies, now, cancellationToken);
        var items = new List<PricePolicySnapshot>(policies.Count);

        foreach (var policy in policies) {
            var referencePrice = referencePrices[policy.TemplateId];
            if (!TryBuildSnapshot(policy, referencePrice, now, out var snapshot)) {
                return AdminResult<PricePolicyPageDto>.Failure(
                    AdminErrors.InternalError("Stored price policy bounds could not be computed.")
                );
            }

            items.Add(snapshot);
        }

        return AdminResult<PricePolicyPageDto>.Success(new PricePolicyPageDto(items, page, pageSize, totalCount));
    }

    public async Task<AdminResult<PricePolicySnapshot>> UpsertAsync(
        Guid templateId,
        Guid actorId,
        string idempotencyKey,
        PricePolicyRequest request,
        CancellationToken cancellationToken
    ) {
        var schemaErrors = ValidateSchema(templateId, actorId, idempotencyKey, request, out var trimmedKey, out var trimmedReason);
        if (schemaErrors.Length > 0) {
            return AdminResult<PricePolicySnapshot>.Failure(
                AdminErrors.ValidationError("One or more validation errors occurred.", schemaErrors)
            );
        }

        var basePrice = request.BasePrice!.Value;
        var bandBasisPoints = request.BandBasisPoints!.Value;
        var expectedVersion = request.ExpectedVersion!.Value;

        var policyErrors = ValidatePolicyRules(basePrice, bandBasisPoints);
        if (policyErrors.Length > 0) {
            return AdminResult<PricePolicySnapshot>.Failure(
                AdminErrors.InvalidPricePolicy("Price policy violates economic bounds rules.", policyErrors)
            );
        }

        var actorKey = actorId.ToString("D");
        var resourceScope = $"price-policy:{templateId:D}";
        var fingerprint = ComputeFingerprint(templateId, basePrice, bandBasisPoints, expectedVersion, trimmedReason);
        var strategy = _db.Database.CreateExecutionStrategy();

        var result = await strategy.ExecuteAsync(async () => {
            for (var attempt = 1; attempt <= MaxConcurrencyRetries; attempt++) {
                _db.ChangeTracker.Clear();

                await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                try {
                    var existingRecord = await _db.HttpIdempotencyRecords
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            r => r.ActorId == actorKey
                                && r.Operation == UpsertOperationName
                                && r.ResourceScope == resourceScope
                                && r.IdempotencyKey == trimmedKey,
                            cancellationToken
                        );

                    if (existingRecord is not null) {
                        if (!string.Equals(existingRecord.RequestFingerprint, fingerprint, StringComparison.Ordinal)) {
                            await transaction.RollbackAsync(cancellationToken);
                            return AdminResult<PricePolicySnapshot>.Failure(AdminErrors.IdempotencyConflict());
                        }

                        var replaySnapshot = JsonSerializer.Deserialize<PricePolicySnapshot>(existingRecord.ResponseBody, JsonOptions);
                        if (replaySnapshot is null) {
                            await transaction.RollbackAsync(cancellationToken);
                            return AdminResult<PricePolicySnapshot>.Failure(
                                AdminErrors.InternalError("Stored idempotency response could not be read.")
                            );
                        }

                        await transaction.CommitAsync(cancellationToken);
                        return AdminResult<PricePolicySnapshot>.Success(replaySnapshot);
                    }

                    var now = DateTimeOffset.UtcNow;
                    var existingPolicy = await _db.PricePolicies
                        .FirstOrDefaultAsync(p => p.TemplateId == templateId, cancellationToken);

                    if (expectedVersion == 0) {
                        if (existingPolicy is not null) {
                            await transaction.RollbackAsync(cancellationToken);
                            return AdminResult<PricePolicySnapshot>.Failure(AdminErrors.VersionConflict(existingPolicy.Version));
                        }

                        existingPolicy = new PricePolicy {
                            TemplateId = templateId,
                            BasePrice = basePrice,
                            BandBasisPoints = bandBasisPoints,
                            Version = 1,
                            UpdatedAt = now
                        };
                        _db.PricePolicies.Add(existingPolicy);
                    } else {
                        if (existingPolicy is null) {
                            await transaction.RollbackAsync(cancellationToken);
                            return AdminResult<PricePolicySnapshot>.Failure(AdminErrors.VersionConflict(0));
                        }

                        if (existingPolicy.Version != expectedVersion) {
                            await transaction.RollbackAsync(cancellationToken);
                            return AdminResult<PricePolicySnapshot>.Failure(AdminErrors.VersionConflict(existingPolicy.Version));
                        }

                        existingPolicy.BasePrice = basePrice;
                        existingPolicy.BandBasisPoints = bandBasisPoints;
                        existingPolicy.Version++;
                        existingPolicy.UpdatedAt = now;
                    }

                    var referencePrice = await ResolveReferencePriceAsync(existingPolicy, now, cancellationToken);
                    if (!TryBuildSnapshot(existingPolicy, referencePrice, now, out var snapshot)) {
                        await transaction.RollbackAsync(cancellationToken);
                        return AdminResult<PricePolicySnapshot>.Failure(
                            AdminErrors.InvalidPricePolicy("Computed price bounds overflow allowed Int64 range.")
                        );
                    }

                    _db.PricePolicyAudits.Add(new PricePolicyAudit {
                        AuditId = Guid.NewGuid(),
                        TemplateId = existingPolicy.TemplateId,
                        ActorId = actorId,
                        BasePrice = existingPolicy.BasePrice,
                        BandBasisPoints = existingPolicy.BandBasisPoints,
                        Version = existingPolicy.Version,
                        Reason = trimmedReason,
                        CreatedAt = now
                    });

                    _db.HttpIdempotencyRecords.Add(new HttpIdempotencyRecord {
                        Id = Guid.NewGuid(),
                        ActorId = actorKey,
                        Operation = UpsertOperationName,
                        ResourceScope = resourceScope,
                        IdempotencyKey = trimmedKey,
                        RequestFingerprint = fingerprint,
                        StatusCode = StatusCodes.Status200OK,
                        ResponseBody = JsonSerializer.Serialize(snapshot, JsonOptions),
                        ResourceId = existingPolicy.TemplateId,
                        CreatedAt = now
                    });

                    await _db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return AdminResult<PricePolicySnapshot>.Success(snapshot);
                } catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyRetries) {
                    await transaction.RollbackAsync(cancellationToken);
                } catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex) && attempt < MaxConcurrencyRetries) {
                    await transaction.RollbackAsync(cancellationToken);
                }
            }

            return AdminResult<PricePolicySnapshot>.Failure(
                AdminErrors.DependencyUnavailable("Could not complete price policy update due to concurrent updates.")
            );
        });

        if (result.IsSuccess && result.Value is not null) {
            await TryRefreshCacheAsync(result.Value, cancellationToken);
        }

        return result;
    }

    public async Task<AdminResult<PricePolicySnapshot>> GetAndRefreshAsync(Guid templateId, CancellationToken cancellationToken) {
        if (templateId == Guid.Empty) {
            return AdminResult<PricePolicySnapshot>.Failure(AdminErrors.PricePolicyNotFound());
        }

        var policy = await _db.PricePolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TemplateId == templateId, cancellationToken);

        if (policy is null) {
            return AdminResult<PricePolicySnapshot>.Failure(AdminErrors.PricePolicyNotFound());
        }

        var now = DateTimeOffset.UtcNow;
        var referencePrice = await ResolveReferencePriceAsync(policy, now, cancellationToken);
        if (!TryBuildSnapshot(policy, referencePrice, now, out var snapshot)) {
            return AdminResult<PricePolicySnapshot>.Failure(
                AdminErrors.InternalError("Stored price policy bounds could not be computed.")
            );
        }

        await TryRefreshCacheAsync(snapshot, cancellationToken);
        return AdminResult<PricePolicySnapshot>.Success(snapshot);
    }

    public async Task WarmCacheAsync(CancellationToken cancellationToken) {
        var policies = await _db.PricePolicies
            .AsNoTracking()
            .OrderBy(p => p.TemplateId)
            .ToListAsync(cancellationToken);

        if (policies.Count == 0) {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var referencePrices = await ResolveReferencePricesAsync(policies, now, cancellationToken);
        var warmed = 0;

        foreach (var policy in policies) {
            if (!TryBuildSnapshot(policy, referencePrices[policy.TemplateId], now, out var snapshot)) {
                continue;
            }

            if (await TryRefreshCacheAsync(snapshot, cancellationToken)) {
                warmed++;
            }
        }

        _logger.LogInformation("Warmed Redis price policy cache for {Warmed}/{Total} templates.", warmed, policies.Count);
    }

    private async Task<bool> TryRefreshCacheAsync(PricePolicySnapshot snapshot, CancellationToken cancellationToken) {
        try {
            await _cache.SetAsync(snapshot, cancellationToken);
            return true;
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to refresh Redis price policy cache for template {TemplateId}.", snapshot.TemplateId);
            return false;
        }
    }

    private async Task<long> ResolveReferencePriceAsync(PricePolicy policy, DateTimeOffset nowUtc, CancellationToken cancellationToken) {
        var map = await ResolveReferencePricesAsync([policy], nowUtc, cancellationToken);
        return map[policy.TemplateId];
    }

    private async Task<Dictionary<Guid, long>> ResolveReferencePricesAsync(
        IReadOnlyList<PricePolicy> policies,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken
    ) {
        var result = policies.ToDictionary(p => p.TemplateId, p => p.BasePrice);
        if (policies.Count == 0) {
            return result;
        }

        var templateIds = policies.Select(p => p.TemplateId).ToList();
        var (dayStartUtc, nextDayStartUtc) = GetBusinessDayUtcRange(nowUtc);

        var todayFacts = await _db.TradeFacts
            .AsNoTracking()
            .Where(tf => templateIds.Contains(tf.TemplateId) && tf.CompletedAt >= dayStartUtc && tf.CompletedAt < nextDayStartUtc)
            .GroupBy(tf => tf.TemplateId)
            .Select(g => new {
                TemplateId = g.Key,
                SumGross = g.Sum(x => x.GrossGem),
                SumQty = g.Sum(x => (long)x.Quantity)
            })
            .ToListAsync(cancellationToken);

        var resolvedIds = new HashSet<Guid>();
        foreach (var agg in todayFacts) {
            if (agg.SumQty > 0) {
                result[agg.TemplateId] = Math.Max(1L, agg.SumGross / agg.SumQty);
                resolvedIds.Add(agg.TemplateId);
            }
        }

        var remainingIds = templateIds.Where(id => !resolvedIds.Contains(id)).ToList();
        if (remainingIds.Count == 0) {
            return result;
        }

        var latestSummaries = await _db.MarketDailySummaries
            .AsNoTracking()
            .Where(s => remainingIds.Contains(s.TemplateId) && s.TradeCount > 0)
            .GroupBy(s => s.TemplateId)
            .Select(g => g.OrderByDescending(x => x.Day).First())
            .ToListAsync(cancellationToken);

        foreach (var summary in latestSummaries) {
            if (summary.ReferencePrice >= 1) {
                result[summary.TemplateId] = summary.ReferencePrice;
                resolvedIds.Add(summary.TemplateId);
            }
        }

        var stillRemaining = templateIds.Where(id => !resolvedIds.Contains(id)).ToList();
        if (stillRemaining.Count == 0) {
            return result;
        }

        var priorFacts = await _db.TradeFacts
            .AsNoTracking()
            .Where(tf => stillRemaining.Contains(tf.TemplateId) && tf.CompletedAt < dayStartUtc)
            .Select(tf => new { tf.TemplateId, tf.GrossGem, tf.Quantity, tf.CompletedAt })
            .ToListAsync(cancellationToken);

        foreach (var group in priorFacts.GroupBy(x => x.TemplateId)) {
            var latestDayGroup = group
                .GroupBy(x => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.CompletedAt, _businessTimeZone).Date))
                .OrderByDescending(g => g.Key)
                .First();

            var sumQty = latestDayGroup.Sum(x => (long)x.Quantity);
            if (sumQty > 0) {
                var sumGross = latestDayGroup.Sum(x => x.GrossGem);
                result[group.Key] = Math.Max(1L, sumGross / sumQty);
            }
        }

        return result;
    }

    private (DateTimeOffset StartUtc, DateTimeOffset EndUtc) GetBusinessDayUtcRange(DateTimeOffset nowUtc) {
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, _businessTimeZone);
        var localStart = new DateTime(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var localEnd = localStart.AddDays(1);

        var startUtc = new DateTimeOffset(localStart, _businessTimeZone.GetUtcOffset(localStart)).ToUniversalTime();
        var endUtc = new DateTimeOffset(localEnd, _businessTimeZone.GetUtcOffset(localEnd)).ToUniversalTime();
        return (startUtc, endUtc);
    }

    private static bool TryBuildSnapshot(
        PricePolicy policy,
        long referencePrice,
        DateTimeOffset asOf,
        out PricePolicySnapshot snapshot
    ) {
        if (!PriceBoundsCalculator.TryComputeBounds(referencePrice, policy.BandBasisPoints, out var minUnitPrice, out var maxUnitPrice)) {
            snapshot = null!;
            return false;
        }

        snapshot = new PricePolicySnapshot(
            policy.TemplateId,
            policy.BasePrice,
            referencePrice,
            policy.BandBasisPoints,
            minUnitPrice,
            maxUnitPrice,
            policy.Version,
            asOf
        );
        return true;
    }

    private static string[] ValidateSchema(
        Guid templateId,
        Guid actorId,
        string idempotencyKey,
        PricePolicyRequest request,
        out string trimmedKey,
        out string trimmedReason
    ) {
        var errors = new List<string>();
        trimmedKey = (idempotencyKey ?? string.Empty).Trim();
        trimmedReason = (request.Reason ?? string.Empty).Trim();

        if (templateId == Guid.Empty) {
            errors.Add("templateId must be a valid non-empty UUID.");
        }

        if (actorId == Guid.Empty) {
            errors.Add("Authenticated actor subject must be a valid non-empty UUID.");
        }

        if (trimmedKey.Length == 0 || trimmedKey.Length > 128) {
            errors.Add("Idempotency-Key header is required and must be between 1 and 128 characters.");
        }

        if (request.BasePrice is null) {
            errors.Add("basePrice is required.");
        }

        if (request.BandBasisPoints is null) {
            errors.Add("bandBasisPoints is required.");
        }

        if (request.ExpectedVersion is null || request.ExpectedVersion < 0) {
            errors.Add("expectedVersion is required and must be greater than or equal to 0.");
        }

        if (trimmedReason.Length == 0 || trimmedReason.Length > 500) {
            errors.Add("reason is required and must be between 1 and 500 characters.");
        }

        return errors.ToArray();
    }

    private static string[] ValidatePolicyRules(long basePrice, int bandBasisPoints) {
        var errors = new List<string>();

        if (basePrice < 1) {
            errors.Add("basePrice must be greater than or equal to 1.");
        }

        if (bandBasisPoints < PriceBoundsCalculator.MinBandBasisPoints || bandBasisPoints > PriceBoundsCalculator.MaxBandBasisPoints) {
            errors.Add("bandBasisPoints must be between 0 and 9999.");
        }

        if (errors.Count == 0 && !PriceBoundsCalculator.TryComputeBounds(basePrice, bandBasisPoints, out _, out _)) {
            errors.Add("basePrice and bandBasisPoints overflow allowed Int64 bounds.");
        }

        return errors.ToArray();
    }

    private static string ComputeFingerprint(
        Guid templateId,
        long basePrice,
        int bandBasisPoints,
        long expectedVersion,
        string trimmedReason
    ) {
        var canonical = $"{templateId:D}|{basePrice}|{bandBasisPoints}|{expectedVersion}|{trimmedReason}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(hash);
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
