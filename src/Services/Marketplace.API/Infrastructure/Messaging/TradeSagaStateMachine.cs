using Marketplace.API.Domain.Entities;
using Marketplace.API.Domain.Enums;
using Marketplace.API.Infrastructure.Persistence;
using Marketplace.Contracts.V1;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Marketplace.API.Application.Common;

namespace Marketplace.API.Infrastructure.Messaging;

public sealed class TradeSagaStateMachine : MassTransitStateMachine<TradeSagaState>
{
    public State HoldingFunds { get; private set; } = null!;
    public State Transferring { get; private set; } = null!;
    public State Settling { get; private set; } = null!;
    public State Finalizing { get; private set; } = null!;
    public State ReversingItem { get; private set; } = null!;
    public State ReleasingFunds { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Failed { get; private set; } = null!;

    public Event<BuyerFundsHeldEvent> BuyerFundsHeld { get; private set; } = null!;
    public Event<BuyerFundsHoldFailedEvent> BuyerFundsHoldFailed { get; private set; } = null!;
    public Event<ItemTransferredEvent> ItemTransferred { get; private set; } = null!;
    public Event<ItemTransferFailedEvent> ItemTransferFailed { get; private set; } = null!;
    public Event<TradeSettledEvent> TradeSettled { get; private set; } = null!;
    public Event<TradeSettlementRejectedEvent> TradeSettlementRejected { get; private set; } = null!;
    public Event<ItemTransferFinalizedEvent> ItemTransferFinalized { get; private set; } = null!;
    public Event<ItemTransferReversedEvent> ItemTransferReversed { get; private set; } = null!;
    public Event<BuyerFundsRefundedEvent> BuyerFundsRefunded { get; private set; } = null!;

    public TradeSagaStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => BuyerFundsHeld, e => { e.CorrelateById(m => m.Message.TradeId); e.OnMissingInstance(m => m.Discard()); });
        Event(() => BuyerFundsHoldFailed, e => { e.CorrelateById(m => m.Message.TradeId); e.OnMissingInstance(m => m.Discard()); });
        Event(() => ItemTransferred, e => { e.CorrelateById(m => m.Message.TradeId); e.OnMissingInstance(m => m.Discard()); });
        Event(() => ItemTransferFailed, e => { e.CorrelateById(m => m.Message.TradeId); e.OnMissingInstance(m => m.Discard()); });
        Event(() => TradeSettled, e => { e.CorrelateById(m => m.Message.TradeId); e.OnMissingInstance(m => m.Discard()); });
        Event(() => TradeSettlementRejected, e => { e.CorrelateById(m => m.Message.TradeId); e.OnMissingInstance(m => m.Discard()); });
        Event(() => ItemTransferFinalized, e => { e.CorrelateById(m => m.Message.TradeId); e.OnMissingInstance(m => m.Discard()); });
        Event(() => ItemTransferReversed, e => { e.CorrelateById(m => m.Message.TradeId); e.OnMissingInstance(m => m.Discard()); });
        Event(() => BuyerFundsRefunded, e => { e.CorrelateById(m => m.Message.TradeId); e.OnMissingInstance(m => m.Discard()); });

        During(HoldingFunds,
            When(BuyerFundsHeld)
                .TransitionTo(Transferring)
                .ThenAsync(async ctx =>
                {
                    var now = DateTimeOffset.UtcNow;
                    var nextOpId = Guid.NewGuid();
                    var db = GetDb(ctx);

                    ctx.Saga.CurrentOperationId = nextOpId;
                    ctx.Saga.RetryCount = 0;
                    ctx.Saga.UpdatedAt = now;

                    await UpdateTradeAsync(db, ctx.Saga.TradeId, TradeStatus.Transferring, null, null, now, ctx.CancellationToken);
                    AddStep(db, ctx.Saga.TradeId, "HoldBuyerFunds", "Succeeded", null, now);
                    AddStep(db, ctx.Saga.TradeId, "TransferItem", "Pending", null, now);

                    var endpoint = await ctx.GetSendEndpoint(QueueNames.InventoryCommandsUri);
                    await endpoint.Send(
                        new TransferItemCommand(
                            CorrelationId: ctx.Saga.TradeId,
                            OperationId: nextOpId,
                            TradeId: ctx.Saga.TradeId,
                            ListingId: ctx.Saga.ListingId,
                            ItemId: ctx.Saga.ItemId,
                            TemplateId: ctx.Saga.TemplateId,
                            SellerId: ctx.Saga.SellerId,
                            BuyerId: ctx.Saga.BuyerId,
                            Quantity: ctx.Saga.Quantity
                        ),
                        ctx.CancellationToken
                    );
                }),

            When(BuyerFundsHoldFailed)
                .TransitionTo(Failed)
                .ThenAsync(async ctx =>
                {
                    var now = DateTimeOffset.UtcNow;
                    var opId = Guid.NewGuid();
                    var failureCode = ctx.Message.FailureCode;
                    var db = GetDb(ctx);

                    ctx.Saga.FailureReason = failureCode;
                    ctx.Saga.CurrentOperationId = opId;
                    ctx.Saga.UpdatedAt = now;

                    await UpdateTradeAsync(db, ctx.Saga.TradeId, TradeStatus.Failed, failureCode, now, now, ctx.CancellationToken);
                    AddStep(db, ctx.Saga.TradeId, "HoldBuyerFunds", "Rejected", failureCode, now);

                    var snapshot = await ReactivateListingAsync(db, ctx.Saga.ListingId, now, ctx.CancellationToken);
                    if (snapshot is not null)
                    {
                        await ctx.Publish(
                            new ListingReactivatedEvent(ctx.Saga.ListingId, opId, now, snapshot),
                            ctx.CancellationToken
                        );
                    }

                    await ctx.Publish(
                        new TradeFailedEvent(ctx.Saga.TradeId, opId, now, ctx.Saga.TradeId, ctx.Saga.ListingId, failureCode, now),
                        ctx.CancellationToken
                    );
                })
        );

        During(Transferring,
            Ignore(BuyerFundsHeld),
            Ignore(BuyerFundsHoldFailed),

            When(ItemTransferred)
                .TransitionTo(Settling)
                .ThenAsync(async ctx =>
                {
                    var now = DateTimeOffset.UtcNow;
                    var nextOpId = Guid.NewGuid();
                    var db = GetDb(ctx);

                    ctx.Saga.CurrentOperationId = nextOpId;
                    ctx.Saga.RetryCount = 0;
                    ctx.Saga.UpdatedAt = now;

                    await UpdateTradeAsync(db, ctx.Saga.TradeId, TradeStatus.Settling, null, null, now, ctx.CancellationToken);
                    AddStep(db, ctx.Saga.TradeId, "TransferItem", "Succeeded", null, now);
                    AddStep(db, ctx.Saga.TradeId, "SettleTrade", "Pending", null, now);

                    var endpoint = await ctx.GetSendEndpoint(QueueNames.WalletCommandsUri);
                    await endpoint.Send(
                        new SettleTradeCommand(
                            CorrelationId: ctx.Saga.TradeId,
                            OperationId: nextOpId,
                            TradeId: ctx.Saga.TradeId,
                            BuyerId: ctx.Saga.BuyerId,
                            SellerId: ctx.Saga.SellerId,
                            GrossGem: ctx.Saga.GrossGem,
                            FeeGem: ctx.Saga.FeeGem,
                            SellerNetGem: ctx.Saga.SellerNetGem
                        ),
                        ctx.CancellationToken
                    );
                }),

            When(ItemTransferFailed)
                .TransitionTo(ReleasingFunds)
                .ThenAsync(async ctx =>
                {
                    var now = DateTimeOffset.UtcNow;
                    var nextOpId = Guid.NewGuid();
                    var failureCode = ctx.Message.FailureCode;
                    var db = GetDb(ctx);

                    ctx.Saga.FailureReason = failureCode;
                    ctx.Saga.CurrentOperationId = nextOpId;
                    ctx.Saga.RetryCount = 0;
                    ctx.Saga.UpdatedAt = now;

                    await UpdateTradeAsync(db, ctx.Saga.TradeId, TradeStatus.ReleasingFunds, failureCode, null, now, ctx.CancellationToken);
                    AddStep(db, ctx.Saga.TradeId, "TransferItem", "Rejected", failureCode, now);
                    AddStep(db, ctx.Saga.TradeId, "RefundBuyerFunds", "Pending", null, now);

                    var endpoint = await ctx.GetSendEndpoint(QueueNames.WalletCommandsUri);
                    await endpoint.Send(
                        new RefundBuyerFundsCommand(
                            CorrelationId: ctx.Saga.TradeId,
                            OperationId: nextOpId,
                            TradeId: ctx.Saga.TradeId,
                            BuyerId: ctx.Saga.BuyerId,
                            Amount: ctx.Saga.GrossGem
                        ),
                        ctx.CancellationToken
                    );
                })
        );

        During(Settling,
            Ignore(BuyerFundsHeld),
            Ignore(ItemTransferred),
            Ignore(ItemTransferFailed),

            When(TradeSettled)
                .TransitionTo(Finalizing)
                .ThenAsync(async ctx =>
                {
                    var now = DateTimeOffset.UtcNow;
                    var nextOpId = Guid.NewGuid();
                    var db = GetDb(ctx);

                    ctx.Saga.CurrentOperationId = nextOpId;
                    ctx.Saga.RetryCount = 0;
                    ctx.Saga.UpdatedAt = now;

                    await UpdateTradeAsync(db, ctx.Saga.TradeId, TradeStatus.Finalizing, null, null, now, ctx.CancellationToken);
                    AddStep(db, ctx.Saga.TradeId, "SettleTrade", "Succeeded", null, now);
                    AddStep(db, ctx.Saga.TradeId, "FinalizeItemTransfer", "Pending", null, now);

                    var endpoint = await ctx.GetSendEndpoint(QueueNames.InventoryCommandsUri);
                    await endpoint.Send(
                        new FinalizeItemTransferCommand(
                            CorrelationId: ctx.Saga.TradeId,
                            OperationId: nextOpId,
                            TradeId: ctx.Saga.TradeId,
                            ListingId: ctx.Saga.ListingId,
                            ItemId: ctx.Saga.ItemId,
                            BuyerId: ctx.Saga.BuyerId
                        ),
                        ctx.CancellationToken
                    );
                }),

            When(TradeSettlementRejected)
                .TransitionTo(ReversingItem)
                .ThenAsync(async ctx =>
                {
                    var now = DateTimeOffset.UtcNow;
                    var nextOpId = Guid.NewGuid();
                    var failureCode = ctx.Message.FailureCode;
                    var db = GetDb(ctx);

                    ctx.Saga.FailureReason = failureCode;
                    ctx.Saga.CurrentOperationId = nextOpId;
                    ctx.Saga.RetryCount = 0;
                    ctx.Saga.UpdatedAt = now;

                    await UpdateTradeAsync(db, ctx.Saga.TradeId, TradeStatus.ReversingItem, failureCode, null, now, ctx.CancellationToken);
                    AddStep(db, ctx.Saga.TradeId, "SettleTrade", "Rejected", failureCode, now);
                    AddStep(db, ctx.Saga.TradeId, "ReverseItemTransfer", "Pending", null, now);

                    var endpoint = await ctx.GetSendEndpoint(QueueNames.InventoryCommandsUri);
                    await endpoint.Send(
                        new ReverseItemTransferCommand(
                            CorrelationId: ctx.Saga.TradeId,
                            OperationId: nextOpId,
                            TradeId: ctx.Saga.TradeId,
                            ListingId: ctx.Saga.ListingId,
                            ItemId: ctx.Saga.ItemId,
                            SellerId: ctx.Saga.SellerId,
                            BuyerId: ctx.Saga.BuyerId
                        ),
                        ctx.CancellationToken
                    );
                })
        );

        During(ReversingItem,
            Ignore(BuyerFundsHeld),
            Ignore(ItemTransferred),
            Ignore(TradeSettlementRejected),

            When(ItemTransferReversed)
                .TransitionTo(ReleasingFunds)
                .ThenAsync(async ctx =>
                {
                    var now = DateTimeOffset.UtcNow;
                    var nextOpId = Guid.NewGuid();
                    var db = GetDb(ctx);

                    ctx.Saga.CurrentOperationId = nextOpId;
                    ctx.Saga.RetryCount = 0;
                    ctx.Saga.UpdatedAt = now;

                    await UpdateTradeAsync(db, ctx.Saga.TradeId, TradeStatus.ReleasingFunds, ctx.Saga.FailureReason, null, now, ctx.CancellationToken);
                    AddStep(db, ctx.Saga.TradeId, "ReverseItemTransfer", "Succeeded", null, now);
                    AddStep(db, ctx.Saga.TradeId, "RefundBuyerFunds", "Pending", null, now);

                    var endpoint = await ctx.GetSendEndpoint(QueueNames.WalletCommandsUri);
                    await endpoint.Send(
                        new RefundBuyerFundsCommand(
                            CorrelationId: ctx.Saga.TradeId,
                            OperationId: nextOpId,
                            TradeId: ctx.Saga.TradeId,
                            BuyerId: ctx.Saga.BuyerId,
                            Amount: ctx.Saga.GrossGem
                        ),
                        ctx.CancellationToken
                    );
                })
        );

        During(ReleasingFunds,
            Ignore(BuyerFundsHeld),
            Ignore(ItemTransferFailed),
            Ignore(ItemTransferReversed),

            When(BuyerFundsRefunded)
                .TransitionTo(Failed)
                .ThenAsync(async ctx =>
                {
                    var now = DateTimeOffset.UtcNow;
                    var opId = Guid.NewGuid();
                    var failureCode = ctx.Saga.FailureReason ?? MarketplaceErrorCodes.TradeFailed;
                    var db = GetDb(ctx);

                    ctx.Saga.CurrentOperationId = opId;
                    ctx.Saga.UpdatedAt = now;

                    await UpdateTradeAsync(db, ctx.Saga.TradeId, TradeStatus.Failed, failureCode, now, now, ctx.CancellationToken);
                    AddStep(db, ctx.Saga.TradeId, "RefundBuyerFunds", "Succeeded", null, now);

                    var snapshot = await ReactivateListingAsync(db, ctx.Saga.ListingId, now, ctx.CancellationToken);
                    if (snapshot is not null)
                    {
                        await ctx.Publish(
                            new ListingReactivatedEvent(ctx.Saga.ListingId, opId, now, snapshot),
                            ctx.CancellationToken
                        );
                    }

                    await ctx.Publish(
                        new TradeFailedEvent(ctx.Saga.TradeId, opId, now, ctx.Saga.TradeId, ctx.Saga.ListingId, failureCode, now),
                        ctx.CancellationToken
                    );
                })
        );

        During(Finalizing,
            Ignore(BuyerFundsHeld),
            Ignore(ItemTransferred),
            Ignore(TradeSettled),

            When(ItemTransferFinalized)
                .TransitionTo(Completed)
                .ThenAsync(async ctx =>
                {
                    var now = DateTimeOffset.UtcNow;
                    var opId = Guid.NewGuid();
                    var db = GetDb(ctx);

                    ctx.Saga.CurrentOperationId = opId;
                    ctx.Saga.UpdatedAt = now;

                    await UpdateTradeAsync(db, ctx.Saga.TradeId, TradeStatus.Completed, null, now, now, ctx.CancellationToken);
                    AddStep(db, ctx.Saga.TradeId, "FinalizeItemTransfer", "Succeeded", null, now);

                    var snapshot = await MarkListingSoldAsync(db, ctx.Saga.ListingId, now, ctx.CancellationToken);
                    if (snapshot is not null)
                    {
                        await ctx.Publish(
                            new TradeCompletedEvent(
                                CorrelationId: ctx.Saga.TradeId,
                                OperationId: opId,
                                OccurredAt: now,
                                Listing: snapshot,
                                TradeId: ctx.Saga.TradeId,
                                BuyerId: ctx.Saga.BuyerId,
                                SellerId: ctx.Saga.SellerId,
                                TemplateId: ctx.Saga.TemplateId,
                                Quantity: ctx.Saga.Quantity,
                                UnitPrice: ctx.Saga.UnitPrice,
                                GrossGem: ctx.Saga.GrossGem,
                                FeeGem: ctx.Saga.FeeGem,
                                SellerNetGem: ctx.Saga.SellerNetGem,
                                CompletedAt: now
                            ),
                            ctx.CancellationToken
                        );
                    }
                })
        );

        During(Completed,
            Ignore(BuyerFundsHeld),
            Ignore(ItemTransferred),
            Ignore(TradeSettled),
            Ignore(ItemTransferFinalized)
        );

        During(Failed,
            Ignore(BuyerFundsHeld),
            Ignore(BuyerFundsHoldFailed),
            Ignore(ItemTransferFailed),
            Ignore(TradeSettlementRejected),
            Ignore(ItemTransferReversed),
            Ignore(BuyerFundsRefunded)
        );
    }

    private static MarketplaceDbContext GetDb<T>(BehaviorContext<TradeSagaState, T> ctx) where T : class =>
        ctx.GetPayload<IServiceProvider>().GetRequiredService<MarketplaceDbContext>();

    private static void AddStep(
        MarketplaceDbContext db,
        Guid tradeId,
        string step,
        string outcome,
        string? errorCode,
        DateTimeOffset at)
    {
        db.SagaSteps.Add(new SagaStep
        {
            StepId = Guid.NewGuid(),
            TradeId = tradeId,
            Step = step,
            Outcome = outcome,
            ErrorCode = errorCode,
            At = at
        });
    }

    private static async Task UpdateTradeAsync(
        MarketplaceDbContext db,
        Guid tradeId,
        TradeStatus status,
        string? failureCode,
        DateTimeOffset? completedAt,
        DateTimeOffset updatedAt,
        CancellationToken ct)
    {
        var trade = await db.TradeHistories.SingleOrDefaultAsync(x => x.TradeId == tradeId, ct);
        if (trade is null)
            return;

        trade.Status = status;
        if (failureCode is not null)
            trade.FailureCode = failureCode;
        if (completedAt.HasValue)
            trade.CompletedAt = completedAt;
        trade.UpdatedAt = updatedAt;
    }

    private static async Task<ListingSnapshot?> ReactivateListingAsync(
        MarketplaceDbContext db,
        Guid listingId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var listing = await db.Listings.SingleOrDefaultAsync(x => x.ListingId == listingId, ct);
        if (listing is null || listing.Status != ListingStatus.Processing)
            return null;

        listing.Status = ListingStatus.Active;
        listing.Version += 1;
        listing.UpdatedAt = now;
        return listing.ToSnapshot();
    }

    private static async Task<ListingSnapshot?> MarkListingSoldAsync(
        MarketplaceDbContext db,
        Guid listingId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var listing = await db.Listings.SingleOrDefaultAsync(x => x.ListingId == listingId, ct);
        if (listing is null || listing.Status != ListingStatus.Processing)
            return null;

        listing.Status = ListingStatus.Sold;
        listing.Version += 1;
        listing.UpdatedAt = now;
        return listing.ToSnapshot();
    }
}
