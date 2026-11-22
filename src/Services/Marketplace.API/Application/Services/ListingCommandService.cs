using System.Security.Cryptography;
using System.Text;
using Marketplace.API.Application.Common;
using Marketplace.API.Application.DTOs;
using Marketplace.API.Application.Interfaces;
using Marketplace.API.Application.Options;
using Marketplace.API.Domain.Entities;
using Marketplace.API.Domain.Services;
using Marketplace.API.Infrastructure.Persistence;
using Marketplace.Contracts.V1;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marketplace.API.Application.Services;

public sealed class ListingCommandService : IListingCommandService
{
    private const string CreateListingOperation = "MKT-01";
    private const string CreateListingScope = "listings";

    private readonly MarketplaceDbContext _db;
    private readonly IAuthStatusClient _authClient;
    private readonly IInventoryClient _inventoryClient;
    private readonly IPricePolicyProvider _pricePolicyProvider;
    private readonly IDistributedListingLock _listingLock;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ISendEndpointProvider _sendEndpointProvider;
    private readonly EconomyOptions _economyOptions;
    private readonly ILogger<ListingCommandService> _logger;

    public ListingCommandService(
        MarketplaceDbContext db,
        IAuthStatusClient authClient,
        IInventoryClient inventoryClient,
        IPricePolicyProvider pricePolicyProvider,
        IDistributedListingLock listingLock,
        IPublishEndpoint publishEndpoint,
        ISendEndpointProvider sendEndpointProvider,
        IOptions<EconomyOptions> economyOptions,
        ILogger<ListingCommandService> logger)
    {
        _db = db;
        _authClient = authClient;
        _inventoryClient = inventoryClient;
        _pricePolicyProvider = pricePolicyProvider;
        _listingLock = listingLock;
        _publishEndpoint = publishEndpoint;
        _sendEndpointProvider = sendEndpointProvider;
        _economyOptions = economyOptions.Value;
        _logger = logger;
    }

    public async Task<CreateListingOutcome> CreateListingAsync(
        Guid sellerId,
        string idempotencyKey,
        CreateListingRequest request,
        CancellationToken ct)
    {
        ValidateCreateRequest(idempotencyKey, request);
        await EnsureAccountAllowedAsync(sellerId, requireTradePermission: true, ct);

        var fingerprint = ComputeFingerprint(request);

        var existingRecord = await FindIdempotencyRecordAsync(sellerId, idempotencyKey, ct);
        if (existingRecord is not null)
        {
            return await ReplayCreateListingAsync(existingRecord, fingerprint, ct);
        }

        var item = await _inventoryClient.GetItemAsync(request.ItemId, ct);
        if (item is null)
            throw new MarketplaceApiException(404, MarketplaceErrorCodes.ItemNotFound, "Item was not found.");

        if (item.OwnerId != sellerId)
            throw new MarketplaceApiException(409, MarketplaceErrorCodes.ItemNotOwned, "Item is not owned by the seller.");

        if (!item.Tradable)
            throw new MarketplaceApiException(409, MarketplaceErrorCodes.ItemNotTradable, "Item is not tradable.");

        if (!string.Equals(item.Status, "Available", StringComparison.Ordinal))
            throw new MarketplaceApiException(409, MarketplaceErrorCodes.ItemLocked, "Item is not available for listing.");

        if (item.Quantity != request.Quantity)
            throw new MarketplaceApiException(409, MarketplaceErrorCodes.QuantityMismatch, "Listing must include the entire item stack.");

        var policy = await _pricePolicyProvider.GetRequiredPolicyAsync(item.TemplateId, ct);
        if (!PriceCalculator.IsWithinBounds(request.UnitPrice, policy.MinUnitPrice, policy.MaxUnitPrice))
        {
            throw new MarketplaceApiException(
                422,
                MarketplaceErrorCodes.PriceOutOfBounds,
                $"Unit price must be between {policy.MinUnitPrice} and {policy.MaxUnitPrice} Gem."
            );
        }

        TradeAmounts amounts;
        try
        {
            amounts = PriceCalculator.ComputeTradeAmounts(request.UnitPrice, request.Quantity, _economyOptions.FeeBasisPoints);
        }
        catch (OverflowException)
        {
            throw new MarketplaceApiException(400, MarketplaceErrorCodes.ValidationError, "Listing total exceeds supported Gem range.");
        }

        var now = DateTimeOffset.UtcNow;
        var listing = new Listing
        {
            ListingId = Guid.NewGuid(),
            SellerId = sellerId,
            ItemId = item.ItemId,
            TemplateId = item.TemplateId,
            ItemName = item.Name,
            Category = item.Category,
            Rarity = item.Rarity,
            Quantity = request.Quantity,
            UnitPrice = request.UnitPrice,
            GrossGem = amounts.GrossGem,
            FeeGem = amounts.FeeGem,
            SellerNetGem = amounts.SellerNetGem,
            FeeBasisPoints = _economyOptions.FeeBasisPoints,
            PolicyVersion = policy.Version,
            Status = ListingStatus.PendingLock,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        var idemRecord = new HttpIdempotencyRecord
        {
            RecordId = Guid.NewGuid(),
            ActorId = sellerId,
            Operation = CreateListingOperation,
            ResourceScope = CreateListingScope,
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = fingerprint,
            ResourceId = listing.ListingId,
            ResponseStatusCode = StatusCodes.Status202Accepted,
            CreatedAt = now,
            UpdatedAt = now
        };

        try
        {
            _db.Listings.Add(listing);
            _db.HttpIdempotencyRecords.Add(idemRecord);
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            var raced = await FindIdempotencyRecordAsync(sellerId, idempotencyKey, ct);
            if (raced is not null)
                return await ReplayCreateListingAsync(raced, fingerprint, ct);
            throw;
        }

        var lockResult = await _inventoryClient.LockItemAsync(
            new LockItemRequestDto(listing.ListingId, listing.ItemId, sellerId, listing.Quantity, listing.TemplateId),
            ct
        );

        if (lockResult.Outcome == LockCallOutcomeType.UnknownFailure)
        {
            return new CreateListingOutcome(StatusCodes.Status202Accepted, ToDto(listing));
        }

        if (lockResult.Outcome == LockCallOutcomeType.BusinessRejected)
        {
            var statusCode = lockResult.StatusCode ?? StatusCodes.Status409Conflict;
            var errorCode = lockResult.ErrorCode ?? MarketplaceErrorCodes.ItemLocked;
            var errorDetail = lockResult.ErrorDetail ?? "Inventory rejected item lock.";

            await MarkListingCreateFailedAsync(listing.ListingId, idemRecord.RecordId, statusCode, errorCode, errorDetail, ct);
            throw new MarketplaceApiException(statusCode, errorCode, errorDetail);
        }

        try
        {
            var lockedItem = lockResult.Data!.Item;
            var activatedAt = DateTimeOffset.UtcNow;

            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var trackedListing = await _db.Listings.SingleAsync(x => x.ListingId == listing.ListingId, ct);
            trackedListing.ItemName = lockedItem.Name;
            trackedListing.Category = lockedItem.Category;
            trackedListing.Rarity = lockedItem.Rarity;
            trackedListing.Status = ListingStatus.Active;
            trackedListing.Version += 1;
            trackedListing.UpdatedAt = activatedAt;

            var trackedIdem = await _db.HttpIdempotencyRecords.SingleAsync(x => x.RecordId == idemRecord.RecordId, ct);
            trackedIdem.ResponseStatusCode = StatusCodes.Status201Created;
            trackedIdem.UpdatedAt = activatedAt;

            await _publishEndpoint.Publish(
                new ListingCreatedEvent(
                    CorrelationId: trackedListing.ListingId,
                    OperationId: Guid.NewGuid(),
                    OccurredAt: activatedAt,
                    Listing: trackedListing.ToSnapshot()
                ),
                ct
            );

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new CreateListingOutcome(StatusCodes.Status201Created, ToDto(trackedListing));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate listing {ListingId} after Inventory lock. Attempting compensation.", listing.ListingId);
            _db.ChangeTracker.Clear();

            var unlocked = await _inventoryClient.TryUnlockItemAsync(
                listing.ListingId,
                new UnlockItemRequestDto(listing.ItemId, sellerId),
                CancellationToken.None
            );

            if (unlocked)
            {
                await MarkListingCreateFailedAsync(
                    listing.ListingId,
                    idemRecord.RecordId,
                    StatusCodes.Status503ServiceUnavailable,
                    MarketplaceErrorCodes.DependencyUnavailable,
                    "Listing activation failed and item lock was compensated.",
                    CancellationToken.None
                );
                throw new MarketplaceApiException(
                    StatusCodes.Status503ServiceUnavailable,
                    MarketplaceErrorCodes.DependencyUnavailable,
                    "Listing could not be activated. Please retry with a new request."
                );
            }

            return new CreateListingOutcome(StatusCodes.Status202Accepted, ToDto(listing));
        }
    }

    public async Task<CancelListingResultDto> CancelListingAsync(Guid sellerId, Guid listingId, CancellationToken ct)
    {
        await EnsureAccountAllowedAsync(sellerId, requireTradePermission: false, ct);

        var precheck = await _db.Listings.AsNoTracking().SingleOrDefaultAsync(x => x.ListingId == listingId, ct);
        if (precheck is null || precheck.SellerId != sellerId)
            throw new MarketplaceApiException(404, MarketplaceErrorCodes.ListingNotFound, "Listing was not found.");

        if (precheck.Status == ListingStatus.Cancelled)
        {
            return new CancelListingResultDto(
                precheck.ListingId,
                nameof(ListingStatus.Cancelled),
                precheck.InventoryUnlockedAt.HasValue ? "Confirmed" : "Pending"
            );
        }

        var lockTtl = TimeSpan.FromSeconds(_economyOptions.ListingLockTtlSeconds);
        await using var distributedLock = await _listingLock.TryAcquireAsync(listingId, lockTtl, ct);
        if (distributedLock is null)
            throw new MarketplaceApiException(409, MarketplaceErrorCodes.ListingBusy, "Listing is currently being processed.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var listing = await _db.Listings.AsNoTracking().SingleOrDefaultAsync(x => x.ListingId == listingId, ct);
        if (listing is null || listing.SellerId != sellerId)
            throw new MarketplaceApiException(404, MarketplaceErrorCodes.ListingNotFound, "Listing was not found.");

        if (listing.Status == ListingStatus.Cancelled)
        {
            return new CancelListingResultDto(
                listing.ListingId,
                nameof(ListingStatus.Cancelled),
                listing.InventoryUnlockedAt.HasValue ? "Confirmed" : "Pending"
            );
        }

        if (listing.Status == ListingStatus.Processing)
            throw new MarketplaceApiException(409, MarketplaceErrorCodes.ListingBusy, "Listing is currently in an active purchase.");

        if (listing.Status != ListingStatus.Active)
            throw new MarketplaceApiException(409, MarketplaceErrorCodes.ListingNotActive, "Only active listings can be cancelled.");

        var now = DateTimeOffset.UtcNow;
        var nextVersion = listing.Version + 1;

        var updatedRows = await _db.Listings
            .Where(x => x.ListingId == listingId
                && x.SellerId == sellerId
                && x.Status == ListingStatus.Active
                && x.Version == listing.Version)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, ListingStatus.Cancelled)
                .SetProperty(x => x.Version, nextVersion)
                .SetProperty(x => x.UpdatedAt, now),
                ct);

        if (updatedRows == 0)
        {
            var latest = await _db.Listings.AsNoTracking().SingleOrDefaultAsync(x => x.ListingId == listingId, ct);
            if (latest?.Status == ListingStatus.Processing)
                throw new MarketplaceApiException(409, MarketplaceErrorCodes.ListingBusy, "Listing is currently being processed.");

            throw new MarketplaceApiException(409, MarketplaceErrorCodes.ListingNotActive, "Listing is no longer active.");
        }

        listing.Status = ListingStatus.Cancelled;
        listing.Version = nextVersion;
        listing.UpdatedAt = now;

        var operationId = Guid.NewGuid();
        var inventoryQueue = await _sendEndpointProvider.GetSendEndpoint(QueueNames.InventoryCommandsUri);

        await inventoryQueue.Send(
            new UnlockItemCommand(
                CorrelationId: listing.ListingId,
                OperationId: operationId,
                ListingId: listing.ListingId,
                ItemId: listing.ItemId,
                SellerId: listing.SellerId
            ),
            ct
        );

        await _publishEndpoint.Publish(
            new ListingCancelledEvent(
                CorrelationId: listing.ListingId,
                OperationId: operationId,
                OccurredAt: now,
                Listing: listing.ToSnapshot()
            ),
            ct
        );

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new CancelListingResultDto(listing.ListingId, nameof(ListingStatus.Cancelled), "Pending");
    }

    public async Task<ListingDto> GetListingAsync(Guid requesterId, Guid listingId, CancellationToken ct)
    {
        await EnsureAccountAllowedAsync(requesterId, requireTradePermission: false, ct);

        var listing = await _db.Listings.AsNoTracking().SingleOrDefaultAsync(x => x.ListingId == listingId, ct);
        if (listing is null)
            throw new MarketplaceApiException(404, MarketplaceErrorCodes.ListingNotFound, "Listing was not found.");

        if (listing.SellerId != requesterId && listing.Status != ListingStatus.Active)
            throw new MarketplaceApiException(404, MarketplaceErrorCodes.ListingNotFound, "Listing was not found.");

        return ToDto(listing);
    }

    public async Task<ListingPageDto> GetMyListingsAsync(
        Guid sellerId,
        ListingStatus status,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        if (page < 1)
            throw new MarketplaceApiException(400, MarketplaceErrorCodes.ValidationError, "Page must be at least 1.");
        if (pageSize is < 1 or > 100)
            throw new MarketplaceApiException(400, MarketplaceErrorCodes.ValidationError, "PageSize must be between 1 and 100.");

        await EnsureAccountAllowedAsync(sellerId, requireTradePermission: false, ct);

        var query = _db.Listings
            .AsNoTracking()
            .Where(x => x.SellerId == sellerId && x.Status == status);

        var totalCount = await query.LongCountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.ListingId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => ToDto(x))
            .ToListAsync(ct);

        return new ListingPageDto(items, page, pageSize, totalCount);
    }

    private async Task EnsureAccountAllowedAsync(Guid userId, bool requireTradePermission, CancellationToken ct)
    {
        var access = await _authClient.GetAccessStatusAsync(userId, ct);

        if (string.Equals(access.Status, "Banned", StringComparison.OrdinalIgnoreCase))
            throw new MarketplaceApiException(403, MarketplaceErrorCodes.AccountBanned, "Account is banned.");

        if (requireTradePermission && (!access.CanTrade || string.Equals(access.Status, "TradeRestricted", StringComparison.OrdinalIgnoreCase)))
            throw new MarketplaceApiException(403, MarketplaceErrorCodes.TradeRestricted, "Account is restricted from trading.");
    }

    private async Task<HttpIdempotencyRecord?> FindIdempotencyRecordAsync(Guid sellerId, string idempotencyKey, CancellationToken ct)
    {
        return await _db.HttpIdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ActorId == sellerId
                    && x.Operation == CreateListingOperation
                    && x.ResourceScope == CreateListingScope
                    && x.IdempotencyKey == idempotencyKey,
                ct
            );
    }

    private async Task<CreateListingOutcome> ReplayCreateListingAsync(
        HttpIdempotencyRecord record,
        string fingerprint,
        CancellationToken ct)
    {
        if (!string.Equals(record.RequestFingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new MarketplaceApiException(
                409,
                MarketplaceErrorCodes.IdempotencyConflict,
                "Idempotency-Key was already used with a different request payload."
            );
        }

        if (record.ResourceId is null)
            throw new MarketplaceApiException(record.ResponseStatusCode, record.ErrorCode ?? MarketplaceErrorCodes.InternalError, record.ErrorDetail ?? "Previous request failed.");

        var listing = await _db.Listings.AsNoTracking().SingleOrDefaultAsync(x => x.ListingId == record.ResourceId.Value, ct);
        if (listing is null)
            throw new MarketplaceApiException(404, MarketplaceErrorCodes.ListingNotFound, "Original listing no longer exists.");

        if (listing.Status == ListingStatus.CreateFailed)
        {
            throw new MarketplaceApiException(
                record.ResponseStatusCode >= 400 ? record.ResponseStatusCode : StatusCodes.Status409Conflict,
                record.ErrorCode ?? MarketplaceErrorCodes.ItemLocked,
                record.ErrorDetail ?? "Listing creation failed."
            );
        }

        var statusCode = listing.Status == ListingStatus.PendingLock
            ? StatusCodes.Status202Accepted
            : StatusCodes.Status201Created;

        return new CreateListingOutcome(statusCode, ToDto(listing));
    }

    private async Task MarkListingCreateFailedAsync(
        Guid listingId,
        Guid idempotencyRecordId,
        int statusCode,
        string errorCode,
        string errorDetail,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var listing = await _db.Listings.SingleOrDefaultAsync(x => x.ListingId == listingId, ct);
        if (listing is not null && listing.Status == ListingStatus.PendingLock)
        {
            listing.Status = ListingStatus.CreateFailed;
            listing.Version += 1;
            listing.UpdatedAt = now;
        }

        var record = await _db.HttpIdempotencyRecords.SingleOrDefaultAsync(x => x.RecordId == idempotencyRecordId, ct);
        if (record is not null)
        {
            record.ResponseStatusCode = statusCode;
            record.ErrorCode = errorCode;
            record.ErrorDetail = errorDetail;
            record.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);
    }

    private static void ValidateCreateRequest(string idempotencyKey, CreateListingRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
            errors.Add("Idempotency-Key header is required and must be 1..128 characters.");
        if (request.ItemId == Guid.Empty)
            errors.Add("itemId must be a non-empty UUID.");
        if (request.Quantity < 1)
            errors.Add("quantity must be at least 1.");
        if (request.UnitPrice < 1)
            errors.Add("unitPrice must be at least 1.");

        if (errors.Count > 0)
            throw new MarketplaceApiException(400, MarketplaceErrorCodes.ValidationError, "Request validation failed.", errors.ToArray());
    }

    private static string ComputeFingerprint(CreateListingRequest request)
    {
        var raw = $"{request.ItemId:D}|{request.Quantity}|{request.UnitPrice}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private static ListingDto ToDto(Listing l) => new(
        l.ListingId,
        l.SellerId,
        l.ItemId,
        l.TemplateId,
        l.ItemName,
        l.Category,
        l.Rarity,
        l.Quantity,
        l.UnitPrice,
        l.GrossGem,
        l.Status,
        l.Version,
        l.CreatedAt,
        l.UpdatedAt
    );
}
