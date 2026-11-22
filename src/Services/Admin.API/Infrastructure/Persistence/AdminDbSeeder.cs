using Admin.API.Application.Options;
using Admin.API.Domain.Entities;
using Admin.API.Domain.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Admin.API.Infrastructure.Persistence;

public sealed class AdminDbSeeder {
    private static readonly Guid SystemSeedActorId = Guid.Parse("10000000-0000-4000-8000-000000000001");

    private readonly AdminDbContext _db;
    private readonly SeedOptions _seedOptions;
    private readonly EconomyOptions _economyOptions;
    private readonly ILogger<AdminDbSeeder> _logger;

    public AdminDbSeeder(
        AdminDbContext db,
        IOptions<SeedOptions> seedOptions,
        IOptions<EconomyOptions> economyOptions,
        ILogger<AdminDbSeeder> logger
    ) {
        _db = db;
        _seedOptions = seedOptions.Value;
        _economyOptions = economyOptions.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken) {
        if (!_seedOptions.Enabled) {
            return;
        }

        var existingIds = await _db.PricePolicies
            .AsNoTracking()
            .Select(p => p.TemplateId)
            .ToHashSetAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var created = 0;

        foreach (var seed in DefaultPricePolicySeeds.TradableTemplates) {
            if (existingIds.Contains(seed.TemplateId)) {
                continue;
            }

            _db.PricePolicies.Add(new PricePolicy {
                TemplateId = seed.TemplateId,
                BasePrice = seed.BasePrice,
                BandBasisPoints = _economyOptions.DefaultBandBps,
                Version = 1,
                UpdatedAt = now
            });

            _db.PricePolicyAudits.Add(new PricePolicyAudit {
                AuditId = Guid.NewGuid(),
                TemplateId = seed.TemplateId,
                ActorId = SystemSeedActorId,
                BasePrice = seed.BasePrice,
                BandBasisPoints = _economyOptions.DefaultBandBps,
                Version = 1,
                Reason = "Initial baseline price policy seed",
                CreatedAt = now
            });

            created++;
        }

        if (created > 0) {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Seeded {Count} AdminAnalyticsDb baseline price policies.", created);
        }
    }
}
