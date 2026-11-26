using Admin.API.Application.Interfaces;
using Admin.API.Application.Options;
using Microsoft.Extensions.Options;

namespace Admin.API.Infrastructure.Background;

public sealed class MarketDailySummaryWorker : BackgroundService {
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeZoneInfo _businessTimeZone;
    private readonly TimeOnly _runTime;
    private readonly ILogger<MarketDailySummaryWorker> _logger;

    public MarketDailySummaryWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<AnalyticsOptions> analyticsOptions,
        ILogger<MarketDailySummaryWorker> logger
    ) {
        _scopeFactory = scopeFactory;
        _businessTimeZone = ResolveTimeZone(analyticsOptions.Value.TimeZoneId);
        _runTime = TimeOnly.TryParse(analyticsOptions.Value.DailySummaryTime, out var parsed)
            ? parsed
            : new TimeOnly(0, 0);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            await RunPreviousDayReconciliationAsync(stoppingToken);
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            return;
        } catch (Exception ex) {
            _logger.LogError(ex, "Startup catch-up reconciliation failed.");
        }

        while (!stoppingToken.IsCancellationRequested) {
            var delay = ComputeDelayUntilNextRun(DateTimeOffset.UtcNow);
            _logger.LogInformation("Next daily market summary reconciliation scheduled in {Delay}.", delay);

            try {
                await Task.Delay(delay, stoppingToken);
                await RunPreviousDayReconciliationAsync(stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                _logger.LogError(ex, "Daily market summary reconciliation failed. Retrying in 5 minutes.");
                try {
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                } catch (OperationCanceledException) {
                    break;
                }
            }
        }
    }

    private async Task RunPreviousDayReconciliationAsync(CancellationToken cancellationToken) {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var analyticsService = scope.ServiceProvider.GetRequiredService<IMarketAnalyticsService>();
        var previousDay = analyticsService.GetCurrentBusinessDay().AddDays(-1);
        await analyticsService.ReconcileDayAsync(previousDay, cancellationToken);
    }

    private TimeSpan ComputeDelayUntilNextRun(DateTimeOffset nowUtc) {
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, _businessTimeZone);
        var todayRunLocal = new DateTime(
            localNow.Year,
            localNow.Month,
            localNow.Day,
            _runTime.Hour,
            _runTime.Minute,
            _runTime.Second,
            DateTimeKind.Unspecified
        );

        var nextRunLocal = localNow.DateTime < todayRunLocal ? todayRunLocal : todayRunLocal.AddDays(1);
        var nextRunUtc = new DateTimeOffset(nextRunLocal, _businessTimeZone.GetUtcOffset(nextRunLocal)).ToUniversalTime();
        var delay = nextRunUtc - nowUtc;
        return delay > TimeSpan.FromSeconds(1) ? delay : TimeSpan.FromSeconds(1);
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId) {
        try {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        } catch (TimeZoneNotFoundException) {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
    }
}
