using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wallet.API.Application.Services;
using Wallet.API.Domain.Enums;
using Wallet.API.Infrastructure.Options;
using Wallet.API.Infrastructure.Payments;
using Wallet.API.Infrastructure.Persistence;

namespace Wallet.API.Infrastructure.BackgroundServices;

public sealed class PaymentReconciliationWorker : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan MinimumOrderAgeBeforeReconcile = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<ZaloPayOptions> _options;
    private readonly ILogger<PaymentReconciliationWorker> _logger;

    public PaymentReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<ZaloPayOptions> options,
        ILogger<PaymentReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalSeconds = _options.Value.GetEffectiveReconcileIntervalSeconds();

            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error during PaymentReconciliationWorker cycle.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RunCycleAsync(CancellationToken stoppingToken)
    {
        var zaloPayOptions = _options.Value;
        if (!zaloPayOptions.ReconciliationEnabled)
        {
            return;
        }

        if (!zaloPayOptions.IsConfiguredForQuery())
        {
            _logger.LogWarning("Skipping payment reconciliation cycle because ZaloPay query configuration is incomplete.");
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        var zaloPayClient = scope.ServiceProvider.GetRequiredService<IZaloPayClient>();
        var settlementService = scope.ServiceProvider.GetRequiredService<ITopUpSettlementService>();

        var cutoff = DateTimeOffset.UtcNow - MinimumOrderAgeBeforeReconcile;
        var pendingOrders = await dbContext.TopUpOrders
            .AsNoTracking()
            .Where(o => o.Status == TopUpOrderStatus.Pending && o.CreatedAt <= cutoff)
            .OrderBy(o => o.CreatedAt)
            .ThenBy(o => o.TopUpId)
            .Take(BatchSize)
            .Select(o => new PendingOrderSnapshot(
                o.TopUpId,
                o.AppTransId,
                o.AmountVnd,
                o.CreatedAt))
            .ToListAsync(stoppingToken);

        if (pendingOrders.Count == 0)
        {
            return;
        }

        var expiryWindow = TimeSpan.FromMinutes(zaloPayOptions.GetEffectiveOrderExpiryMinutes());

        foreach (var order in pendingOrders)
        {
            stoppingToken.ThrowIfCancellationRequested();

            try
            {
                await ReconcileSingleOrderAsync(
                    order,
                    expiryWindow,
                    zaloPayClient,
                    settlementService,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Error reconciling pending top-up order TopUpId={TopUpId}, AppTransId={AppTransId}. Keeping Pending for next cycle.",
                    order.TopUpId,
                    order.AppTransId);
            }
        }
    }

    private async Task ReconcileSingleOrderAsync(
        PendingOrderSnapshot order,
        TimeSpan expiryWindow,
        IZaloPayClient zaloPayClient,
        ITopUpSettlementService settlementService,
        CancellationToken stoppingToken)
    {
        var queryResult = await zaloPayClient.QueryOrderAsync(order.AppTransId, stoppingToken);

        switch (queryResult.Outcome)
        {
            case ZaloPayQueryOutcome.Paid:
                {
                    if (queryResult.Amount != order.AmountVnd || queryResult.ZpTransId <= 0)
                    {
                        _logger.LogWarning(
                            "Reconciliation query returned Paid with mismatched amount or invalid ZpTransId. TopUpId={TopUpId}, AppTransId={AppTransId}, ExpectedAmountVnd={ExpectedAmountVnd}, ProviderAmountVnd={ProviderAmountVnd}, ZpTransId={ZpTransId}. Keeping Pending for review.",
                            order.TopUpId,
                            order.AppTransId,
                            order.AmountVnd,
                            queryResult.Amount,
                            queryResult.ZpTransId);
                        return;
                    }

                    var settlement = await settlementService.SettleTopUpAsync(
                        new TopUpSettlementRequest(
                            AppTransId: order.AppTransId,
                            ZpTransId: queryResult.ZpTransId,
                            AmountVnd: queryResult.Amount,
                            ExpectedUserId: null,
                            Source: "reconciliation"),
                        stoppingToken);

                    if (settlement.Outcome == TopUpSettlementOutcome.Discrepancy)
                    {
                        _logger.LogWarning(
                            "Reconciliation settlement flagged discrepancy for TopUpId={TopUpId}, AppTransId={AppTransId}: {Reason}",
                            order.TopUpId,
                            order.AppTransId,
                            settlement.Reason);
                    }

                    return;
                }

            case ZaloPayQueryOutcome.Failed:
                {
                    var orderAge = DateTimeOffset.UtcNow - order.CreatedAt;
                    var terminalStatus = orderAge >= expiryWindow
                        ? TopUpOrderStatus.Expired
                        : TopUpOrderStatus.Failed;

                    await settlementService.MarkTerminalFailureAsync(
                        order.TopUpId,
                        terminalStatus,
                        stoppingToken);
                    return;
                }

            case ZaloPayQueryOutcome.Processing:
                {
                    var orderAge = DateTimeOffset.UtcNow - order.CreatedAt;
                    if (orderAge >= expiryWindow)
                    {
                        _logger.LogWarning(
                            "Top-up order remains unresolved/processing past expiry window. TopUpId={TopUpId}, AppTransId={AppTransId}, ReturnCode={ReturnCode}, SubReturnCode={SubReturnCode}, IsProcessing={IsProcessing}. Keeping Pending.",
                            order.TopUpId,
                            order.AppTransId,
                            queryResult.ReturnCode,
                            queryResult.SubReturnCode,
                            queryResult.IsProcessing);
                    }

                    return;
                }

            case ZaloPayQueryOutcome.TransientError:
            default:
                {
                    _logger.LogWarning(
                        "Reconciliation query encountered transient error for TopUpId={TopUpId}, AppTransId={AppTransId}. Keeping Pending.",
                        order.TopUpId,
                        order.AppTransId);
                    return;
                }
        }
    }

    private sealed record PendingOrderSnapshot(
        Guid TopUpId,
        string AppTransId,
        long AmountVnd,
        DateTimeOffset CreatedAt);
}