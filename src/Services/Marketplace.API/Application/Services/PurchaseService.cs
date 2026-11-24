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

public sealed class PurchaseService : IPurchaseService
{
    private const string PurchaseOperation = "MKT-03";
    private const string PurchaseScope = "purchase";

    private readonly MarketplaceDbContext _db;
    private readonly IAuthStatusClient _authClient;
    private readonly IDistributedListingLock _listingLock;
    private readonly ISendEndpointProvider _sendEndpointProvider;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly EconomyOptions _economyOptions;

    public PurchaseService(
        MarketplaceDbContext db,
        IAuthStatusClient authClient,
        IDistributedListingLock listingLock,
        ISendEndpointProvider sendEndpointProvider,
        IPublishEndpoint publishEndpoint,
        IOptions<EconomyOptions> economyOptions)
    {
        _db = db;
        _authClient = authClient;
        _listingLock = listingLock;
        _sendEndpointProvider = sendEndpointProvider;
        _publishEndpoint = publishEndpoint;
        _economyOptions = economyOptions.Value;
    }

    public async Task<PurchaseAcceptedDto> PurchaseListingAsync(
        Guid buyerId,
        Guid listingId,
        string idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
        {
            throw new MarketplaceApiException(
                400,
                MarketplaceErrorCodes.ValidationError,
                "Idempotency-Key header is required and must be 1..128 characters."
            );
        }

        await EnsureTradeAllowedAsync(buyerId, ct);

        var fingerprint = listingId.ToString("D");
        var existingRecord = await FindIdempotencyRecordAsync(buyerId, idempotencyKey, ct);
        if (existingRecord is not null)
        {
            return await ReplayPurchaseAsync(existingRecord, fingerprint, ct);
        }

        var lockTtl = TimeSpan.FromSeconds(_economyOptions.ListingLockTtlSeconds);
        await using var distributedLock = await _listingLock.TryAcquireAsync(listingId, lockTtl, ct);
        if (distributedLock is null)
            throw new MarketplaceApiException(409, MarketplaceErrorCodes.ListingBusy, "Listing is currently being processed.");

        var replayInsideLock = await FindIdempotencyRecordAsync(buyerId, idempotencyKey, ct);
        if (replayInsideLock is not null)
            return await ReplayPurchaseAsync(replayInsideLock, fingerprint, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var listing = await _db.Listings.AsNoTracking().SingleOrDefaultAsync(x => x.ListingId == listingId, ct);
        if (listing is null)
            throw new MarketplaceApiException(404, MarketplaceErrorCodes.ListingNotFound, "Listing was not found.");

        if (listing.SellerId == buyerId)
            throw new MarketplaceApiException(409, MarketplaceErrorCodes.SelfPurchase, "You cannot purchase your own listing.");

        if (listing.Status == ListingStatus.Processing)
            throw new MarketplaceApiException(409, MarketplaceErrorCodes.ListingBusy, "Listing is currently being processed.");

        if (listing.Status != ListingStatus.Active)
            throw new MarketplaceApiException(409, MarketplaceErrorCodes.ListingNotActive, "Listing is not active.");

        var now = DateTimeOffset.UtcNow;
        var nextVersion = listing.Version + 1;

        var updatedRows = await _db.Listings
            .Where(x => x.ListingId == listingId
                && x.Status == ListingStatus.Active
                && x.Version == listing.Version)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, ListingStatus.Processing)
                .SetProperty(x => x.Version, nextVersion)
                .SetProperty(x => x.UpdatedAt, now),
                ct);

        if (updatedRows == 0)
        {
            var latest = await _db.Listings.AsNoTracking().SingleOrDefaultAsync(x => x.ListingId == listingId, ct);
            if (latest?.Status == ListingStatus.Processing)
                throw new MarketplaceApiException(409, MarketplaceErrorCodes.ListingBusy, "Listing is currently being processed.");

            throw new MarketplaceApiException(409, MarketplaceErrorCodes.ListingNotActive, "Listing is not active.");
        }

        listing.Status = ListingStatus.Processing;
        listing.Version = nextVersion;
        listing.UpdatedAt = now;

        var tradeId = Guid.NewGuid();
        var holdOperationId = Guid.NewGuid();

        var trade = new TradeHistory
        {
            TradeId = tradeId,
            ListingId = listing.ListingId,
            BuyerId = buyerId,
            SellerId = listing.SellerId,
            ItemId = listing.ItemId,
            TemplateId = listing.TemplateId,
            Quantity = listing.Quantity,
            UnitPrice = listing.UnitPrice,
            GrossGem = listing.GrossGem,
            FeeGem = listing.FeeGem,
            SellerNetGem = listing.SellerNetGem,
            Status = TradeStatus.HoldingFunds,
            FailureCode = null,
            RequiresReview = false,
            IdempotencyKey = idempotencyKey,
            CreatedAt = now,
            UpdatedAt = now,
            CompletedAt = null
        };

        var sagaState = new TradeSagaState
        {
            CorrelationId = tradeId,
            TradeId = tradeId,
            ListingId = listing.ListingId,
            BuyerId = buyerId,
            SellerId = listing.SellerId,
            ItemId = listing.ItemId,
            TemplateId = listing.TemplateId,
            Quantity = listing.Quantity,
            UnitPrice = listing.UnitPrice,
            GrossGem = listing.GrossGem,
            FeeGem = listing.FeeGem,
            SellerNetGem = listing.SellerNetGem,
            CurrentState = nameof(TradeStatus.HoldingFunds),
            CurrentOperationId = holdOperationId,
            RetryCount = 0,
            RequiresReview = false,
            FailureReason = null,
            UpdatedAt = now
        };

        var initialStep = new SagaStep
        {
            StepId = Guid.NewGuid(),
            TradeId = tradeId,
            Step = "HoldBuyerFunds",
            Outcome = "Pending",
            ErrorCode = null,
            At = now
        };

        var idemRecord = new HttpIdempotencyRecord
        {
            RecordId = Guid.NewGuid(),
            ActorId = buyerId,
            Operation = PurchaseOperation,
            ResourceScope = PurchaseScope,
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = fingerprint,
            ResourceId = tradeId,
            ResponseStatusCode = StatusCodes.Status202Accepted,
            CreatedAt = now,
            UpdatedAt = now
        };

        try
        {
            _db.TradeHistories.Add(trade);
            _db.SagaStates.Add(sagaState);
            _db.SagaSteps.Add(initialStep);
            _db.HttpIdempotencyRecords.Add(idemRecord);

            var walletQueue = await _sendEndpointProvider.GetSendEndpoint(QueueNames.WalletCommandsUri);
            await walletQueue.Send(
                new HoldBuyerFundsCommand(
                    CorrelationId: tradeId,
                    OperationId: holdOperationId,
                    TradeId: tradeId,
                    BuyerId: buyerId,
                    Amount: listing.GrossGem
                ),
                ct
            );

            await _publishEndpoint.Publish(
                new ListingProcessingEvent(
                    CorrelationId: listing.ListingId,
                    OperationId: holdOperationId,
                    OccurredAt: now,
                    Listing: listing.ToSnapshot()
                ),
                ct
            );

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();

            var raced = await FindIdempotencyRecordAsync(buyerId, idempotencyKey, ct);
            if (raced is not null)
                return await ReplayPurchaseAsync(raced, fingerprint, ct);

            throw;
        }

        return ToAcceptedDto(trade);
    }

    private async Task EnsureTradeAllowedAsync(Guid userId, CancellationToken ct)
    {
        var access = await _authClient.GetAccessStatusAsync(userId, ct);

        if (string.Equals(access.Status, "Banned", StringComparison.OrdinalIgnoreCase))
            throw new MarketplaceApiException(403, MarketplaceErrorCodes.AccountBanned, "Account is banned.");

        if (!access.CanTrade || string.Equals(access.Status, "TradeRestricted", StringComparison.OrdinalIgnoreCase))
            throw new MarketplaceApiException(403, MarketplaceErrorCodes.TradeRestricted, "Account is restricted from trading.");
    }

    private async Task<HttpIdempotencyRecord?> FindIdempotencyRecordAsync(Guid buyerId, string idempotencyKey, CancellationToken ct)
    {
        return await _db.HttpIdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ActorId == buyerId
                    && x.Operation == PurchaseOperation
                    && x.ResourceScope == PurchaseScope
                    && x.IdempotencyKey == idempotencyKey,
                ct
            );
    }

    private async Task<PurchaseAcceptedDto> ReplayPurchaseAsync(
        HttpIdempotencyRecord record,
        string fingerprint,
        CancellationToken ct)
    {
        if (!string.Equals(record.RequestFingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new MarketplaceApiException(
                409,
                MarketplaceErrorCodes.IdempotencyConflict,
                "Idempotency-Key was already used for a different listing."
            );
        }

        if (record.ResourceId is null)
            throw new MarketplaceApiException(404, MarketplaceErrorCodes.TradeNotFound, "Original trade was not found.");

        var trade = await _db.TradeHistories.AsNoTracking().SingleOrDefaultAsync(x => x.TradeId == record.ResourceId.Value, ct);
        if (trade is null)
            throw new MarketplaceApiException(404, MarketplaceErrorCodes.TradeNotFound, "Original trade was not found.");

        return ToAcceptedDto(trade);
    }

    private static PurchaseAcceptedDto ToAcceptedDto(TradeHistory t) => new(
        t.TradeId,
        t.ListingId,
        t.Status,
        t.GrossGem,
        t.FeeGem,
        t.SellerNetGem,
        $"/api/v1/marketplace/trades/{t.TradeId}"
    );
}
