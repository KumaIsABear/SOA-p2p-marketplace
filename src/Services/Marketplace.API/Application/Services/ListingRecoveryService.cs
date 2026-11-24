using Marketplace.API.Application.Common;
using Marketplace.API.Application.DTOs;
using Marketplace.API.Application.Interfaces;
using Marketplace.API.Application.Options;
using Marketplace.API.Domain.Entities;
using Marketplace.API.Domain.Enums;
using Marketplace.API.Infrastructure.Persistence;
using Marketplace.Contracts.V1;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marketplace.API.Application.Services;

public sealed class ListingRecoveryService : IListingRecoveryService
{
    private const string CreateListingOperation = "MKT-01";
    private const string RetriesExhaustedCode = "RECOVERY_RETRIES_EXHAUSTED";

    private readonly MarketplaceDbContext _db;
    private readonly IInventoryClient _inventoryClient;
    private readonly IWalletClient _walletClient;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ISendEndpointProvider _sendEndpointProvider;
    private readonly RecoveryOptions _options;
    private readonly ILogger<ListingRecoveryService> _logger;

    public ListingRecoveryService(
        MarketplaceDbContext db,
        IInventoryClient inventoryClient,
        IWalletClient walletClient,
        IPublishEndpoint publishEndpoint,
        ISendEndpointProvider sendEndpointProvider,
        IOptions<RecoveryOptions> options,
        ILogger<ListingRecoveryService> logger)
    {
        _db = db;
        _inventoryClient = inventoryClient;
        _walletClient = walletClient;
        _publishEndpoint = publishEndpoint;
        _sendEndpointProvider = sendEndpointProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> RecoverPendingListingsAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-_options.PendingLockGraceSeconds);

        var candidates = await _db.Listings
            .AsNoTracking()
            .Where(x => x.Status == ListingStatus.PendingLock && x.UpdatedAt <= cutoff)
            .OrderBy(x => x.UpdatedAt)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        var processed = 0;

        foreach (var candidate in candidates)
        {
            var lockStatus = await _inventoryClient.GetLockStatusAsync(candidate.ListingId, ct);
            if (lockStatus is null)
                continue;

            if (string.Equals(lockStatus.Status, "Locked", StringComparison.OrdinalIgnoreCase))
            {
                if (await ActivatePendingListingAsync(candidate.ListingId, cutoff, ct))
                    processed++;
                continue;
            }

            if (string.Equals(lockStatus.Status, "Released", StringComparison.OrdinalIgnoreCase))
            {
                if (await FailPendingListingAsync(
                    candidate.ListingId,
                    cutoff,
                    StatusCodes.Status409Conflict,
                    MarketplaceErrorCodes.ItemLocked,
                    "Inventory lock was already released.",
                    ct))
                {
                    processed++;
                }
                continue;
            }

            if (string.Equals(lockStatus.Status, "NotFound", StringComparison.OrdinalIgnoreCase))
            {
                var tombstoned = await _inventoryClient.TryUnlockItemAsync(
                    candidate.ListingId,
                    new UnlockItemRequestDto(candidate.ItemId, candidate.SellerId),
                    ct
                );

                if (!tombstoned)
                    continue;

                if (await FailPendingListingAsync(
                    candidate.ListingId,
                    cutoff,
                    StatusCodes.Status503ServiceUnavailable,
                    MarketplaceErrorCodes.DependencyUnavailable,
                    "Inventory lock was not confirmed and listing creation was closed.",
                    ct))
                {
                    processed++;
                }
            }
        }

        return processed;
    }

    public async Task<int> RecoverStalledSagasAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-_options.SagaStaleSeconds);

        var candidates = await _db.SagaStates
            .AsNoTracking()
            .Where(x => x.CurrentState != nameof(TradeStatus.Completed)
                && x.CurrentState != nameof(TradeStatus.Failed)
                && !x.RequiresReview
                && x.UpdatedAt <= cutoff)
            .OrderBy(x => x.UpdatedAt)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        var processed = 0;

        foreach (var candidate in candidates)
        {
            WalletTradeTraceDto? walletTrace = null;
            ItemTransferTraceDto? inventoryTrace = null;

            if (candidate.CurrentState is nameof(TradeStatus.Settling) or nameof(TradeStatus.ReleasingFunds))
                walletTrace = await _walletClient.GetTradeTraceAsync(candidate.TradeId, ct);
            else if (candidate.CurrentState is nameof(TradeStatus.Finalizing) or nameof(TradeStatus.ReversingItem))
                inventoryTrace = await _inventoryClient.GetTransferTraceAsync(candidate.TradeId, ct);

            if (await ProcessSingleSagaAsync(candidate.TradeId, cutoff, walletTrace, inventoryTrace, ct))
                processed++;
        }

        return processed;
    }

    private async Task<bool> ActivatePendingListingAsync(Guid listingId, DateTimeOffset cutoff, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var listing = await _db.Listings.SingleOrDefaultAsync(x => x.ListingId == listingId, ct);
        if (listing is null || listing.Status != ListingStatus.PendingLock || listing.UpdatedAt > cutoff)
            return false;

        var now = DateTimeOffset.UtcNow;
        listing.Status = ListingStatus.Active;
        listing.Version += 1;
        listing.UpdatedAt = now;

        var idem = await _db.HttpIdempotencyRecords
            .SingleOrDefaultAsync(x => x.ResourceId == listingId && x.Operation == CreateListingOperation, ct);

        if (idem is not null)
        {
            idem.ResponseStatusCode = StatusCodes.Status201Created;
            idem.ErrorCode = null;
            idem.ErrorDetail = null;
            idem.UpdatedAt = now;
        }

        await _publishEndpoint.Publish(
            new ListingCreatedEvent(
                CorrelationId: listing.ListingId,
                OperationId: Guid.NewGuid(),
                OccurredAt: now,
                Listing: listing.ToSnapshot()
            ),
            ct
        );

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Recovered PendingLock listing {ListingId} to Active.", listingId);
        return true;
    }

    private async Task<bool> FailPendingListingAsync(
        Guid listingId,
        DateTimeOffset cutoff,
        int statusCode,
        string errorCode,
        string errorDetail,
        CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var listing = await _db.Listings.SingleOrDefaultAsync(x => x.ListingId == listingId, ct);
        if (listing is null || listing.Status != ListingStatus.PendingLock || listing.UpdatedAt > cutoff)
            return false;

        var now = DateTimeOffset.UtcNow;
        listing.Status = ListingStatus.CreateFailed;
        listing.Version += 1;
        listing.UpdatedAt = now;

        var idem = await _db.HttpIdempotencyRecords
            .SingleOrDefaultAsync(x => x.ResourceId == listingId && x.Operation == CreateListingOperation, ct);

        if (idem is not null)
        {
            idem.ResponseStatusCode = statusCode;
            idem.ErrorCode = errorCode;
            idem.ErrorDetail = errorDetail;
            idem.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Recovered PendingLock listing {ListingId} to CreateFailed ({ErrorCode}).", listingId, errorCode);
        return true;
    }

    private async Task<bool> ProcessSingleSagaAsync(
        Guid tradeId,
        DateTimeOffset cutoff,
        WalletTradeTraceDto? walletTrace,
        ItemTransferTraceDto? inventoryTrace,
        CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var saga = await _db.SagaStates.SingleOrDefaultAsync(x => x.TradeId == tradeId, ct);
        if (saga is null
            || saga.RequiresReview
            || saga.CurrentState is nameof(TradeStatus.Completed) or nameof(TradeStatus.Failed)
            || saga.UpdatedAt > cutoff)
        {
            return false;
        }

        var trade = await _db.TradeHistories.SingleOrDefaultAsync(x => x.TradeId == tradeId, ct);
        if (trade is null)
            return false;

        var now = DateTimeOffset.UtcNow;

        if (saga.CurrentState == nameof(TradeStatus.Settling)
            && string.Equals(walletTrace?.SettlementStatus, "Settled", StringComparison.OrdinalIgnoreCase))
        {
            var nextOpId = Guid.NewGuid();
            saga.CurrentState = nameof(TradeStatus.Finalizing);
            saga.CurrentOperationId = nextOpId;
            saga.RetryCount = 0;
            saga.UpdatedAt = now;

            trade.Status = TradeStatus.Finalizing;
            trade.UpdatedAt = now;

            AddStep(tradeId, "SettleTrade", "Succeeded", null, now);
            AddStep(tradeId, "FinalizeItemTransfer", "Pending", null, now);

            var endpoint = await _sendEndpointProvider.GetSendEndpoint(QueueNames.InventoryCommandsUri);
            await endpoint.Send(
                new FinalizeItemTransferCommand(tradeId, nextOpId, tradeId, saga.ListingId, saga.ItemId, saga.BuyerId),
                ct
            );

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }

        if (saga.CurrentState == nameof(TradeStatus.Finalizing)
            && string.Equals(inventoryTrace?.Status, "Finalized", StringComparison.OrdinalIgnoreCase))
        {
            var opId = Guid.NewGuid();
            saga.CurrentState = nameof(TradeStatus.Completed);
            saga.CurrentOperationId = opId;
            saga.UpdatedAt = now;

            trade.Status = TradeStatus.Completed;
            trade.CompletedAt = now;
            trade.UpdatedAt = now;

            AddStep(tradeId, "FinalizeItemTransfer", "Succeeded", null, now);

            var listing = await _db.Listings.SingleOrDefaultAsync(x => x.ListingId == saga.ListingId, ct);
            if (listing is not null && listing.Status == ListingStatus.Processing)
            {
                listing.Status = ListingStatus.Sold;
                listing.Version += 1;
                listing.UpdatedAt = now;

                await _publishEndpoint.Publish(
                    new TradeCompletedEvent(
                        CorrelationId: tradeId,
                        OperationId: opId,
                        OccurredAt: now,
                        Listing: listing.ToSnapshot(),
                        TradeId: tradeId,
                        BuyerId: saga.BuyerId,
                        SellerId: saga.SellerId,
                        TemplateId: saga.TemplateId,
                        Quantity: saga.Quantity,
                        UnitPrice: saga.UnitPrice,
                        GrossGem: saga.GrossGem,
                        FeeGem: saga.FeeGem,
                        SellerNetGem: saga.SellerNetGem,
                        CompletedAt: now
                    ),
                    ct
                );
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }

        if (saga.CurrentState == nameof(TradeStatus.ReversingItem)
            && string.Equals(inventoryTrace?.Status, "Reversed", StringComparison.OrdinalIgnoreCase))
        {
            var nextOpId = Guid.NewGuid();
            saga.CurrentState = nameof(TradeStatus.ReleasingFunds);
            saga.CurrentOperationId = nextOpId;
            saga.RetryCount = 0;
            saga.UpdatedAt = now;

            trade.Status = TradeStatus.ReleasingFunds;
            trade.UpdatedAt = now;

            AddStep(tradeId, "ReverseItemTransfer", "Succeeded", null, now);
            AddStep(tradeId, "RefundBuyerFunds", "Pending", null, now);

            var endpoint = await _sendEndpointProvider.GetSendEndpoint(QueueNames.WalletCommandsUri);
            await endpoint.Send(
                new RefundBuyerFundsCommand(tradeId, nextOpId, tradeId, saga.BuyerId, saga.GrossGem),
                ct
            );

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }

        if (saga.CurrentState == nameof(TradeStatus.ReleasingFunds)
            && string.Equals(walletTrace?.Hold?.Status, "Released", StringComparison.OrdinalIgnoreCase))
        {
            var opId = Guid.NewGuid();
            var failureCode = saga.FailureReason ?? MarketplaceErrorCodes.TradeFailed;

            saga.CurrentState = nameof(TradeStatus.Failed);
            saga.CurrentOperationId = opId;
            saga.UpdatedAt = now;

            trade.Status = TradeStatus.Failed;
            trade.FailureCode = failureCode;
            trade.CompletedAt = now;
            trade.UpdatedAt = now;

            AddStep(tradeId, "RefundBuyerFunds", "Succeeded", null, now);

            var listing = await _db.Listings.SingleOrDefaultAsync(x => x.ListingId == saga.ListingId, ct);
            if (listing is not null && listing.Status == ListingStatus.Processing)
            {
                listing.Status = ListingStatus.Active;
                listing.Version += 1;
                listing.UpdatedAt = now;

                await _publishEndpoint.Publish(
                    new ListingReactivatedEvent(saga.ListingId, opId, now, listing.ToSnapshot()),
                    ct
                );
            }

            await _publishEndpoint.Publish(
                new TradeFailedEvent(tradeId, opId, now, tradeId, saga.ListingId, failureCode, now),
                ct
            );

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }

        if (saga.RetryCount >= _options.MaxSagaRetries)
        {
            saga.RequiresReview = true;
            saga.UpdatedAt = now;
            trade.RequiresReview = true;
            trade.UpdatedAt = now;

            AddStep(tradeId, saga.CurrentState, "Unknown", RetriesExhaustedCode, now);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogWarning("Saga {TradeId} in state {State} exhausted retries and was flagged for review.", tradeId, saga.CurrentState);
            return true;
        }

        await RedriveCurrentOperationAsync(saga, now, ct);
        saga.RetryCount += 1;
        saga.UpdatedAt = now;
        trade.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private async Task RedriveCurrentOperationAsync(TradeSagaState saga, DateTimeOffset now, CancellationToken ct)
    {
        switch (saga.CurrentState)
        {
            case nameof(TradeStatus.HoldingFunds):
                AddStep(saga.TradeId, "HoldBuyerFunds", "Retrying", null, now);
                var holdQueue = await _sendEndpointProvider.GetSendEndpoint(QueueNames.WalletCommandsUri);
                await holdQueue.Send(
                    new HoldBuyerFundsCommand(saga.TradeId, saga.CurrentOperationId, saga.TradeId, saga.BuyerId, saga.GrossGem),
                    ct
                );
                break;

            case nameof(TradeStatus.Transferring):
                AddStep(saga.TradeId, "TransferItem", "Retrying", null, now);
                var transferQueue = await _sendEndpointProvider.GetSendEndpoint(QueueNames.InventoryCommandsUri);
                await transferQueue.Send(
                    new TransferItemCommand(
                        saga.TradeId,
                        saga.CurrentOperationId,
                        saga.TradeId,
                        saga.ListingId,
                        saga.ItemId,
                        saga.TemplateId,
                        saga.SellerId,
                        saga.BuyerId,
                        saga.Quantity
                    ),
                    ct
                );
                break;

            case nameof(TradeStatus.Settling):
                AddStep(saga.TradeId, "SettleTrade", "Retrying", null, now);
                var settleQueue = await _sendEndpointProvider.GetSendEndpoint(QueueNames.WalletCommandsUri);
                await settleQueue.Send(
                    new SettleTradeCommand(
                        saga.TradeId,
                        saga.CurrentOperationId,
                        saga.TradeId,
                        saga.BuyerId,
                        saga.SellerId,
                        saga.GrossGem,
                        saga.FeeGem,
                        saga.SellerNetGem
                    ),
                    ct
                );
                break;

            case nameof(TradeStatus.Finalizing):
                AddStep(saga.TradeId, "FinalizeItemTransfer", "Retrying", null, now);
                var finalizeQueue = await _sendEndpointProvider.GetSendEndpoint(QueueNames.InventoryCommandsUri);
                await finalizeQueue.Send(
                    new FinalizeItemTransferCommand(
                        saga.TradeId,
                        saga.CurrentOperationId,
                        saga.TradeId,
                        saga.ListingId,
                        saga.ItemId,
                        saga.BuyerId
                    ),
                    ct
                );
                break;

            case nameof(TradeStatus.ReversingItem):
                AddStep(saga.TradeId, "ReverseItemTransfer", "Retrying", null, now);
                var reverseQueue = await _sendEndpointProvider.GetSendEndpoint(QueueNames.InventoryCommandsUri);
                await reverseQueue.Send(
                    new ReverseItemTransferCommand(
                        saga.TradeId,
                        saga.CurrentOperationId,
                        saga.TradeId,
                        saga.ListingId,
                        saga.ItemId,
                        saga.SellerId,
                        saga.BuyerId
                    ),
                    ct
                );
                break;

            case nameof(TradeStatus.ReleasingFunds):
                AddStep(saga.TradeId, "RefundBuyerFunds", "Retrying", null, now);
                var refundQueue = await _sendEndpointProvider.GetSendEndpoint(QueueNames.WalletCommandsUri);
                await refundQueue.Send(
                    new RefundBuyerFundsCommand(saga.TradeId, saga.CurrentOperationId, saga.TradeId, saga.BuyerId, saga.GrossGem),
                    ct
                );
                break;
        }
    }

    private void AddStep(Guid tradeId, string step, string outcome, string? errorCode, DateTimeOffset at)
    {
        _db.SagaSteps.Add(new SagaStep
        {
            StepId = Guid.NewGuid(),
            TradeId = tradeId,
            Step = step,
            Outcome = outcome,
            ErrorCode = errorCode,
            At = at
        });
    }
}
