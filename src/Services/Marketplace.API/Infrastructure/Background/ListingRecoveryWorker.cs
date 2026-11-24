using Marketplace.API.Application.Interfaces;
using Marketplace.API.Application.Options;
using Microsoft.Extensions.Options;

namespace Marketplace.API.Infrastructure.Background;

public sealed class ListingRecoveryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RecoveryOptions _options;
    private readonly ILogger<ListingRecoveryWorker> _logger;

    public ListingRecoveryWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<RecoveryOptions> options,
        ILogger<ListingRecoveryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var recovery = scope.ServiceProvider.GetRequiredService<IListingRecoveryService>();

                await recovery.RecoverPendingListingsAsync(stoppingToken);
                await recovery.RecoverStalledSagasAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ListingRecoveryWorker iteration failed.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
