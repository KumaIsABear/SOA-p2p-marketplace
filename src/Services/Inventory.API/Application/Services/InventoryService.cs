using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inventory.API.Application.DTOs;
using Inventory.API.Application.Exceptions;
using Inventory.API.Domain.Entities;
using Inventory.API.Domain.Enums;
using Inventory.API.Domain.Errors;
using Inventory.API.Infrastructure.Options;
using Inventory.API.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.API.Application.Services;

public sealed class InventoryService : IInventoryService
{
    private const string GrantOperationName = "INV-I01:GrantItem";
    private const string GrantResourceScope = "inventory:items";
    private const int MaxConcurrencyRetries = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly InventoryDbContext _dbContext;
    private readonly InventoryOptions _options;

    public InventoryService(InventoryDbContext dbContext, IOptions<InventoryOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public async Task<InventoryViewDto> GetPlayerInventoryAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var inventory = await EnsureInventoryExistsAsync(userId, cancellationToken);

        var items = await _dbContext.InventoryItems
            .AsNoTracking()
            .Include(x => x.Template)
            .Where(x => x.OwnerId == userId)
            .OrderBy(x => x.ItemId)
            .ToListAsync(cancellationToken);

        var itemDtos = items.Select(MapToItemDto).ToList();
        var usedSlots = itemDtos.Count + inventory.ReservedSlots;

        return new InventoryViewDto(
            inventory.UserId,
            inventory.Capacity,
            usedSlots,
            itemDtos);
    }

    public async Task<InventoryItemDto> GrantItemAsync(
        string actorId,
        string idempotencyKey,
        GrantItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateGrantRequest(idempotencyKey, request);

        var fingerprint = ComputeGrantFingerprint(request);

        for (var attempt = 1; attempt <= MaxConcurrencyRetries; attempt++)
        {
            _dbContext.ChangeTracker.Clear();

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

            try
            {
                var existingRecord = await _dbContext.HttpIdempotencyRecords
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x => x.ActorId == actorId
                            && x.Operation == GrantOperationName
                            && x.ResourceScope == GrantResourceScope
                            && x.IdempotencyKey == idempotencyKey,
                        cancellationToken);

                if (existingRecord is not null)
                {
                    if (!string.Equals(existingRecord.RequestFingerprint, fingerprint, StringComparison.Ordinal))
                    {
                        throw new InventoryApiException(
                            StatusCodes.Status409Conflict,
                            InventoryErrorCodes.IdempotencyConflict,
                            "Idempotency-Key was already used with a different request payload.");
                    }

                    await transaction.CommitAsync(cancellationToken);
                    var replayDto = JsonSerializer.Deserialize<InventoryItemDto>(existingRecord.ResponseBody, JsonOptions);
                    if (replayDto is null)
                    {
                        throw new InventoryApiException(
                            StatusCodes.Status500InternalServerError,
                            InventoryErrorCodes.InternalError,
                            "Stored idempotency response could not be read.");
                    }

                    return replayDto;
                }

                var template = await _dbContext.ItemTemplates
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TemplateId == request.TemplateId, cancellationToken);

                if (template is null)
                {
                    throw new InventoryApiException(
                        StatusCodes.Status404NotFound,
                        InventoryErrorCodes.TemplateNotFound,
                        "Item template was not found.");
                }

                var inventory = await _dbContext.Inventories
                    .FirstOrDefaultAsync(x => x.UserId == request.UserId, cancellationToken);

                if (inventory is null)
                {
                    inventory = new Domain.Entities.Inventory
                    {
                        UserId = request.UserId,
                        Capacity = _options.DefaultCapacity,
                        ReservedSlots = 0,
                        Version = 1
                    };
                    _dbContext.Inventories.Add(inventory);
                }

                var existingItemCount = await _dbContext.InventoryItems
                    .CountAsync(x => x.OwnerId == request.UserId, cancellationToken);

                var usedSlots = existingItemCount + inventory.ReservedSlots;
                if (usedSlots + 1 > inventory.Capacity)
                {
                    throw new InventoryApiException(
                        StatusCodes.Status409Conflict,
                        InventoryErrorCodes.InventoryFull,
                        "Target player inventory has no available slots.");
                }

                var item = new InventoryItem
                {
                    ItemId = Guid.NewGuid(),
                    OwnerId = request.UserId,
                    TemplateId = template.TemplateId,
                    Quantity = request.Quantity,
                    Status = InventoryItemStatus.Available,
                    ListingId = null,
                    TradeId = null,
                    Version = 1
                };

                inventory.Version++;
                _dbContext.InventoryItems.Add(item);

                var dto = new InventoryItemDto(
                    item.ItemId,
                    item.OwnerId,
                    template.TemplateId,
                    template.Name,
                    template.Category,
                    template.Rarity,
                    template.Tradable,
                    item.Quantity,
                    item.Status,
                    item.ListingId,
                    item.TradeId);

                var idempotencyRecord = new HttpIdempotencyRecord
                {
                    Id = Guid.NewGuid(),
                    ActorId = actorId,
                    Operation = GrantOperationName,
                    ResourceScope = GrantResourceScope,
                    IdempotencyKey = idempotencyKey,
                    RequestFingerprint = fingerprint,
                    StatusCode = StatusCodes.Status201Created,
                    ResponseBody = JsonSerializer.Serialize(dto, JsonOptions),
                    ResourceId = item.ItemId,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                _dbContext.HttpIdempotencyRecords.Add(idempotencyRecord);

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return dto;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyRetries)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex) && attempt < MaxConcurrencyRetries)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
        }

        throw new InventoryApiException(
            StatusCodes.Status503ServiceUnavailable,
            InventoryErrorCodes.DependencyUnavailable,
            "Could not complete item grant due to concurrent updates.");
    }

    public async Task<InventoryItemDto> GetItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        if (itemId == Guid.Empty)
        {
            throw new InventoryApiException(
                StatusCodes.Status404NotFound,
                InventoryErrorCodes.ItemNotFound,
                "Inventory item was not found.");
        }

        var item = await _dbContext.InventoryItems
            .AsNoTracking()
            .Include(x => x.Template)
            .FirstOrDefaultAsync(x => x.ItemId == itemId, cancellationToken);

        if (item is null)
        {
            throw new InventoryApiException(
                StatusCodes.Status404NotFound,
                InventoryErrorCodes.ItemNotFound,
                "Inventory item was not found.");
        }

        return MapToItemDto(item);
    }

    public async Task<LockItemResultDto> LockItemAsync(LockItemRequest request, CancellationToken cancellationToken = default)
    {
        ValidateLockRequest(request);

        for (var attempt = 1; attempt <= MaxConcurrencyRetries; attempt++)
        {
            _dbContext.ChangeTracker.Clear();

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

            try
            {
                var existingLock = await _dbContext.ItemLocks
                    .FirstOrDefaultAsync(x => x.ListingId == request.ListingId, cancellationToken);

                if (existingLock is not null)
                {
                    if (existingLock.Status == ItemLockStatusType.Released)
                    {
                        throw new InventoryApiException(
                            StatusCodes.Status409Conflict,
                            InventoryErrorCodes.IdempotencyConflict,
                            "Lock for this listing has already been released and cannot be recreated.");
                    }

                    var isExactReplay = existingLock.ItemId == request.ItemId
                        && existingLock.SellerId == request.SellerId
                        && existingLock.TemplateId == request.TemplateId
                        && existingLock.Quantity == request.Quantity;

                    if (!isExactReplay)
                    {
                        throw new InventoryApiException(
                            StatusCodes.Status409Conflict,
                            InventoryErrorCodes.IdempotencyConflict,
                            "Listing lock already exists with different parameters.");
                    }

                    var lockedItem = await _dbContext.InventoryItems
                        .AsNoTracking()
                        .Include(x => x.Template)
                        .FirstOrDefaultAsync(x => x.ItemId == existingLock.ItemId, cancellationToken);

                    if (lockedItem is null)
                    {
                        throw new InventoryApiException(
                            StatusCodes.Status404NotFound,
                            InventoryErrorCodes.ItemNotFound,
                            "Inventory item was not found.");
                    }

                    await transaction.CommitAsync(cancellationToken);
                    return new LockItemResultDto(existingLock.ListingId, MapToItemDto(lockedItem), existingLock.LockedAt);
                }

                var item = await _dbContext.InventoryItems
                    .Include(x => x.Template)
                    .FirstOrDefaultAsync(x => x.ItemId == request.ItemId, cancellationToken);

                if (item is null)
                {
                    throw new InventoryApiException(
                        StatusCodes.Status404NotFound,
                        InventoryErrorCodes.ItemNotFound,
                        "Inventory item was not found.");
                }

                if (item.OwnerId != request.SellerId)
                {
                    throw new InventoryApiException(
                        StatusCodes.Status409Conflict,
                        InventoryErrorCodes.ItemNotOwned,
                        "The specified seller does not own this item.");
                }

                if (item.TemplateId != request.TemplateId)
                {
                    throw new InventoryApiException(
                        StatusCodes.Status400BadRequest,
                        InventoryErrorCodes.ValidationError,
                        "TemplateId does not match the inventory item.",
                        errors: ["templateId does not match the target inventory item."]);
                }

                if (!item.Template.Tradable)
                {
                    throw new InventoryApiException(
                        StatusCodes.Status409Conflict,
                        InventoryErrorCodes.ItemNotTradable,
                        "This item is not tradable.");
                }

                if (item.Status != InventoryItemStatus.Available)
                {
                    throw new InventoryApiException(
                        StatusCodes.Status409Conflict,
                        InventoryErrorCodes.ItemLocked,
                        "This item is already locked or pending settlement.");
                }

                if (item.Quantity != request.Quantity)
                {
                    throw new InventoryApiException(
                        StatusCodes.Status409Conflict,
                        InventoryErrorCodes.QuantityMismatch,
                        "Requested lock quantity must match the whole item stack quantity.");
                }

                var now = DateTimeOffset.UtcNow;

                item.Status = InventoryItemStatus.Locked;
                item.ListingId = request.ListingId;
                item.Version++;

                var itemLock = new ItemLock
                {
                    ListingId = request.ListingId,
                    ItemId = item.ItemId,
                    SellerId = request.SellerId,
                    TemplateId = item.TemplateId,
                    Quantity = item.Quantity,
                    Status = ItemLockStatusType.Locked,
                    LockedAt = now,
                    UpdatedAt = now
                };

                _dbContext.ItemLocks.Add(itemLock);

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return new LockItemResultDto(itemLock.ListingId, MapToItemDto(item), now);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyRetries)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex) && attempt < MaxConcurrencyRetries)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
        }

        throw new InventoryApiException(
            StatusCodes.Status409Conflict,
            InventoryErrorCodes.ItemLocked,
            "Item state changed concurrently while attempting to acquire lock.");
    }

    public async Task<ItemLockStatusDto> UnlockItemAsync(
        Guid listingId,
        UnlockItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateUnlockRequest(listingId, request);

        for (var attempt = 1; attempt <= MaxConcurrencyRetries; attempt++)
        {
            _dbContext.ChangeTracker.Clear();

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

            try
            {
                var now = DateTimeOffset.UtcNow;
                var existingLock = await _dbContext.ItemLocks
                    .FirstOrDefaultAsync(x => x.ListingId == listingId, cancellationToken);

                if (existingLock is not null)
                {
                    if (existingLock.ItemId != request.ItemId || existingLock.SellerId != request.SellerId)
                    {
                        throw new InventoryApiException(
                            StatusCodes.Status409Conflict,
                            InventoryErrorCodes.ItemLockMismatch,
                            "Lock ownership or item does not match the unlock request.");
                    }

                    if (existingLock.Status == ItemLockStatusType.Released)
                    {
                        await transaction.CommitAsync(cancellationToken);
                        return new ItemLockStatusDto(listingId, existingLock.ItemId, ItemLockLookupStatus.Released);
                    }

                    var item = await _dbContext.InventoryItems
                        .FirstOrDefaultAsync(x => x.ItemId == existingLock.ItemId, cancellationToken);

                    if (item is null)
                    {
                        throw new InventoryApiException(
                            StatusCodes.Status409Conflict,
                            InventoryErrorCodes.ItemLockMismatch,
                            "Locked inventory item was not found.");
                    }

                    if (item.Status == InventoryItemStatus.PendingSettlement)
                    {
                        throw new InventoryApiException(
                            StatusCodes.Status409Conflict,
                            InventoryErrorCodes.TransferPending,
                            "Item transfer is pending settlement and cannot be unlocked.");
                    }

                    if (item.OwnerId != request.SellerId)
                    {
                        throw new InventoryApiException(
                            StatusCodes.Status409Conflict,
                            InventoryErrorCodes.ItemLockMismatch,
                            "Item owner does not match the unlock request.");
                    }

                    if (item.Status == InventoryItemStatus.Locked)
                    {
                        if (item.ListingId != listingId)
                        {
                            throw new InventoryApiException(
                                StatusCodes.Status409Conflict,
                                InventoryErrorCodes.ItemLockMismatch,
                                "Item is currently locked by a different listing.");
                        }

                        item.Status = InventoryItemStatus.Available;
                        item.ListingId = null;
                        item.Version++;
                    }

                    existingLock.Status = ItemLockStatusType.Released;
                    existingLock.UpdatedAt = now;

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return new ItemLockStatusDto(listingId, existingLock.ItemId, ItemLockLookupStatus.Released);
                }

                var targetItem = await _dbContext.InventoryItems
                    .FirstOrDefaultAsync(x => x.ItemId == request.ItemId, cancellationToken);

                if (targetItem is null || targetItem.OwnerId != request.SellerId)
                {
                    throw new InventoryApiException(
                        StatusCodes.Status409Conflict,
                        InventoryErrorCodes.ItemLockMismatch,
                        "Target item was not found or is not owned by the specified seller.");
                }

                if (targetItem.Status == InventoryItemStatus.PendingSettlement && targetItem.ListingId == listingId)
                {
                    throw new InventoryApiException(
                        StatusCodes.Status409Conflict,
                        InventoryErrorCodes.TransferPending,
                        "Item transfer is pending settlement and cannot be unlocked.");
                }

                var tombstone = new ItemLock
                {
                    ListingId = listingId,
                    ItemId = targetItem.ItemId,
                    SellerId = request.SellerId,
                    TemplateId = targetItem.TemplateId,
                    Quantity = targetItem.Quantity,
                    Status = ItemLockStatusType.Released,
                    LockedAt = now,
                    UpdatedAt = now
                };

                _dbContext.ItemLocks.Add(tombstone);

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return new ItemLockStatusDto(listingId, targetItem.ItemId, ItemLockLookupStatus.Released);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyRetries)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex) && attempt < MaxConcurrencyRetries)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
        }

        throw new InventoryApiException(
            StatusCodes.Status503ServiceUnavailable,
            InventoryErrorCodes.DependencyUnavailable,
            "Could not complete item unlock due to concurrent updates.");
    }

    public async Task<ItemLockStatusDto> GetLockStatusAsync(Guid listingId, CancellationToken cancellationToken = default)
    {
        var existingLock = await _dbContext.ItemLocks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ListingId == listingId, cancellationToken);

        if (existingLock is null)
        {
            return new ItemLockStatusDto(listingId, null, ItemLockLookupStatus.NotFound);
        }

        var status = existingLock.Status == ItemLockStatusType.Locked
            ? ItemLockLookupStatus.Locked
            : ItemLockLookupStatus.Released;

        return new ItemLockStatusDto(existingLock.ListingId, existingLock.ItemId, status);
    }

    private async Task<Domain.Entities.Inventory> EnsureInventoryExistsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Inventories
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var created = new Domain.Entities.Inventory
        {
            UserId = userId,
            Capacity = _options.DefaultCapacity,
            ReservedSlots = 0,
            Version = 1
        };

        _dbContext.Inventories.Add(created);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return created;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _dbContext.Entry(created).State = EntityState.Detached;

            return await _dbContext.Inventories
                .AsNoTracking()
                .FirstAsync(x => x.UserId == userId, cancellationToken);
        }
    }

    private static void ValidateGrantRequest(string idempotencyKey, GrantItemRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
        {
            errors.Add("Idempotency-Key header is required and must be between 1 and 128 characters.");
        }

        if (request.UserId == Guid.Empty)
        {
            errors.Add("userId must be a valid non-empty UUID.");
        }

        if (request.TemplateId == Guid.Empty)
        {
            errors.Add("templateId must be a valid non-empty UUID.");
        }

        if (request.Quantity < 1)
        {
            errors.Add("quantity must be greater than or equal to 1.");
        }

        if (errors.Count > 0)
        {
            throw new InventoryApiException(
                StatusCodes.Status400BadRequest,
                InventoryErrorCodes.ValidationError,
                "One or more validation errors occurred.",
                errors: errors);
        }
    }

    private static void ValidateLockRequest(LockItemRequest request)
    {
        var errors = new List<string>();

        if (request.ListingId == Guid.Empty)
        {
            errors.Add("listingId must be a valid non-empty UUID.");
        }

        if (request.ItemId == Guid.Empty)
        {
            errors.Add("itemId must be a valid non-empty UUID.");
        }

        if (request.SellerId == Guid.Empty)
        {
            errors.Add("sellerId must be a valid non-empty UUID.");
        }

        if (request.TemplateId == Guid.Empty)
        {
            errors.Add("templateId must be a valid non-empty UUID.");
        }

        if (request.Quantity < 1)
        {
            errors.Add("quantity must be greater than or equal to 1.");
        }

        if (errors.Count > 0)
        {
            throw new InventoryApiException(
                StatusCodes.Status400BadRequest,
                InventoryErrorCodes.ValidationError,
                "One or more validation errors occurred.",
                errors: errors);
        }
    }

    private static void ValidateUnlockRequest(Guid listingId, UnlockItemRequest request)
    {
        var errors = new List<string>();

        if (listingId == Guid.Empty)
        {
            errors.Add("listingId must be a valid non-empty UUID.");
        }

        if (request.ItemId == Guid.Empty)
        {
            errors.Add("itemId must be a valid non-empty UUID.");
        }

        if (request.SellerId == Guid.Empty)
        {
            errors.Add("sellerId must be a valid non-empty UUID.");
        }

        if (errors.Count > 0)
        {
            throw new InventoryApiException(
                StatusCodes.Status400BadRequest,
                InventoryErrorCodes.ValidationError,
                "One or more validation errors occurred.",
                errors: errors);
        }
    }

    private static string ComputeGrantFingerprint(GrantItemRequest request)
    {
        var canonical = $"{request.UserId:D}|{request.TemplateId:D}|{request.Quantity}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash);
    }

    private static InventoryItemDto MapToItemDto(InventoryItem item) => new(
        item.ItemId,
        item.OwnerId,
        item.TemplateId,
        item.Template.Name,
        item.Template.Category,
        item.Template.Rarity,
        item.Template.Tradable,
        item.Quantity,
        item.Status,
        item.ListingId,
        item.TradeId);

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is SqlException sqlEx && sqlEx.Number is 2601 or 2627;
    }
    public async Task<ItemTransferTraceDto> GetTradeTransferTraceAsync(
        Guid tradeId,
        CancellationToken cancellationToken = default)
    {
        if (tradeId == Guid.Empty)
        {
            throw new InventoryApiException(
                StatusCodes.Status404NotFound,
                InventoryErrorCodes.TransferNotFound,
                "Item transfer record was not found.");
        }

        var transfer = await _dbContext.ItemTransfers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TradeId == tradeId, cancellationToken);

        if (transfer is null)
        {
            throw new InventoryApiException(
                StatusCodes.Status404NotFound,
                InventoryErrorCodes.TransferNotFound,
                "Item transfer record was not found.");
        }

        return new ItemTransferTraceDto(
            transfer.TradeId,
            transfer.ItemId,
            transfer.SellerId,
            transfer.BuyerId,
            transfer.Quantity,
            transfer.Status,
            transfer.UpdatedAt);
    }
}