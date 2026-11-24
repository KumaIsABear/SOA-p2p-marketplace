using System.Data;
using Inventory.API.Application.Exceptions;
using Inventory.API.Domain.Entities;
using Inventory.API.Domain.Enums;
using Inventory.API.Domain.Errors;
using Inventory.API.Infrastructure.Options;
using Inventory.API.Infrastructure.Persistence;
using Marketplace.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.API.Application.Services;

public sealed class InventorySagaService : IInventorySagaService
{
    private readonly InventoryDbContext _dbContext;
    private readonly InventoryOptions _options;

    public InventorySagaService(InventoryDbContext dbContext, IOptions<InventoryOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public Task<TransferItemSagaResult> HandleTransferItemAsync(
        TransferItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateTransferCommand(command);

        return ExecuteInLocalTransactionAsync(async ct =>
        {
            var fastReplay = await TryGetTransferReplayAsync(command, asNoTracking: true, ct);
            if (fastReplay is not null)
            {
                return fastReplay;
            }

            var firstUserId = command.SellerId.CompareTo(command.BuyerId) < 0
                ? command.SellerId
                : command.BuyerId;
            var secondUserId = firstUserId == command.SellerId
                ? command.BuyerId
                : command.SellerId;

            var firstInventory = firstUserId == command.BuyerId
                ? await EnsureAndLockInventoryRowAsync(firstUserId, ct)
                : await LockInventoryRowAsync(firstUserId, ct);

            var secondInventory = secondUserId == command.BuyerId
                ? await EnsureAndLockInventoryRowAsync(secondUserId, ct)
                : await LockInventoryRowAsync(secondUserId, ct);

            var sellerInventory = firstUserId == command.SellerId ? firstInventory : secondInventory;
            var buyerInventory = firstUserId == command.BuyerId ? firstInventory : secondInventory;

            if (buyerInventory is null)
            {
                throw new InventoryInvariantViolationException(
                    $"Buyer inventory {command.BuyerId} could not be initialized.");
            }

            var lockedReplay = await TryGetTransferReplayAsync(command, asNoTracking: false, ct);
            if (lockedReplay is not null)
            {
                return lockedReplay;
            }

            var now = DateTimeOffset.UtcNow;

            var item = await _dbContext.InventoryItems
                .FirstOrDefaultAsync(x => x.ItemId == command.ItemId, ct);

            var itemLock = await _dbContext.ItemLocks
                .FirstOrDefaultAsync(x => x.ListingId == command.ListingId, ct);

            var lockValid = sellerInventory is not null
                && item is not null
                && item.OwnerId == command.SellerId
                && item.Status == InventoryItemStatus.Locked
                && item.ListingId == command.ListingId
                && item.TemplateId == command.TemplateId
                && item.Quantity == command.Quantity
                && itemLock is not null
                && itemLock.Status == ItemLockStatusType.Locked
                && itemLock.ItemId == command.ItemId
                && itemLock.SellerId == command.SellerId;

            if (!lockValid)
            {
                return await PersistTransferRejectionAsync(
                    command,
                    InventoryErrorCodes.ItemLockMismatch,
                    now,
                    ct);
            }

            var buyerItemCount = await _dbContext.InventoryItems
                .CountAsync(x => x.OwnerId == command.BuyerId, ct);

            var buyerUsedSlots = buyerItemCount + buyerInventory.ReservedSlots;
            if (buyerUsedSlots >= buyerInventory.Capacity)
            {
                return await PersistTransferRejectionAsync(
                    command,
                    InventoryErrorCodes.InventoryFull,
                    now,
                    ct);
            }

            item!.OwnerId = command.BuyerId;
            item.Status = InventoryItemStatus.PendingSettlement;
            item.TradeId = command.TradeId;
            item.Version++;

            sellerInventory!.ReservedSlots += 1;
            sellerInventory.Version++;

            buyerInventory.Version++;

            var transfer = new ItemTransfer
            {
                TradeId = command.TradeId,
                ItemId = item.ItemId,
                SellerId = command.SellerId,
                BuyerId = command.BuyerId,
                Quantity = command.Quantity,
                Status = ItemTransferStatus.PendingSettlement,
                UpdatedAt = now
            };

            _dbContext.ItemTransfers.Add(transfer);
            await _dbContext.SaveChangesAsync(ct);

            return TransferItemSagaResult.Succeeded(new ItemTransferredEvent(
                command.CorrelationId,
                command.OperationId,
                now,
                command.TradeId,
                command.ListingId,
                item.ItemId,
                command.BuyerId,
                item.Quantity));
        }, cancellationToken);
    }

    public Task<ItemTransferFinalizedEvent> HandleFinalizeItemTransferAsync(
        FinalizeItemTransferCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateFinalizeCommand(command);

        return ExecuteInLocalTransactionAsync(async ct =>
        {
            var transferPeek = await _dbContext.ItemTransfers
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TradeId == command.TradeId, ct);

            if (transferPeek is null)
            {
                throw new InventoryInvariantViolationException(
                    $"ItemTransfer for trade {command.TradeId} was not found.");
            }

            var sellerInventory = await LockInventoryRowAsync(transferPeek.SellerId, ct);

            var transfer = await _dbContext.ItemTransfers
                .FirstOrDefaultAsync(x => x.TradeId == command.TradeId, ct);

            if (transfer is null)
            {
                throw new InventoryInvariantViolationException(
                    $"ItemTransfer for trade {command.TradeId} was not found.");
            }

            if (transfer.ItemId != command.ItemId || transfer.BuyerId != command.BuyerId)
            {
                throw new InventoryInvariantViolationException(
                    $"Finalize command payload does not match ItemTransfer for trade {command.TradeId}.");
            }

            var now = DateTimeOffset.UtcNow;

            if (transfer.Status == ItemTransferStatus.Finalized)
            {
                return new ItemTransferFinalizedEvent(
                    command.CorrelationId,
                    command.OperationId,
                    now,
                    command.TradeId,
                    command.ListingId,
                    command.ItemId);
            }

            if (transfer.Status != ItemTransferStatus.PendingSettlement)
            {
                throw new InventoryInvariantViolationException(
                    $"Cannot finalize trade {command.TradeId} from status {transfer.Status}.");
            }

            if (sellerInventory is null)
            {
                throw new InventoryInvariantViolationException(
                    $"Seller inventory {transfer.SellerId} was not found for trade {command.TradeId}.");
            }

            var item = await _dbContext.InventoryItems
                .FirstOrDefaultAsync(x => x.ItemId == command.ItemId, ct);

            if (item is null
                || item.OwnerId != command.BuyerId
                || item.Status != InventoryItemStatus.PendingSettlement
                || item.TradeId != command.TradeId
                || item.ListingId != command.ListingId)
            {
                throw new InventoryInvariantViolationException(
                    $"Item {command.ItemId} is not in PendingSettlement state matching trade {command.TradeId}.");
            }

            var itemLock = await _dbContext.ItemLocks
                .FirstOrDefaultAsync(x => x.ListingId == command.ListingId, ct);

            if (itemLock is null || itemLock.ItemId != command.ItemId || itemLock.SellerId != transfer.SellerId)
            {
                throw new InventoryInvariantViolationException(
                    $"ItemLock for listing {command.ListingId} does not match trade {command.TradeId}.");
            }

            item.Status = InventoryItemStatus.Available;
            item.ListingId = null;
            item.TradeId = null;
            item.Version++;

            itemLock.Status = ItemLockStatusType.Released;
            itemLock.UpdatedAt = now;

            sellerInventory.ReservedSlots = Math.Max(0, sellerInventory.ReservedSlots - 1);
            sellerInventory.Version++;

            transfer.Status = ItemTransferStatus.Finalized;
            transfer.UpdatedAt = now;

            await _dbContext.SaveChangesAsync(ct);

            return new ItemTransferFinalizedEvent(
                command.CorrelationId,
                command.OperationId,
                now,
                command.TradeId,
                command.ListingId,
                command.ItemId);
        }, cancellationToken);
    }

    public Task<ItemTransferReversedEvent> HandleReverseItemTransferAsync(
        ReverseItemTransferCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateReverseCommand(command);

        return ExecuteInLocalTransactionAsync(async ct =>
        {
            var firstUserId = command.SellerId.CompareTo(command.BuyerId) < 0
                ? command.SellerId
                : command.BuyerId;
            var secondUserId = firstUserId == command.SellerId
                ? command.BuyerId
                : command.SellerId;

            var firstInventory = await LockInventoryRowAsync(firstUserId, ct);
            var secondInventory = await LockInventoryRowAsync(secondUserId, ct);

            var sellerInventory = firstUserId == command.SellerId ? firstInventory : secondInventory;
            var buyerInventory = firstUserId == command.BuyerId ? firstInventory : secondInventory;

            var transfer = await _dbContext.ItemTransfers
                .FirstOrDefaultAsync(x => x.TradeId == command.TradeId, ct);

            if (transfer is null)
            {
                throw new InventoryInvariantViolationException(
                    $"ItemTransfer for trade {command.TradeId} was not found.");
            }

            if (transfer.ItemId != command.ItemId
                || transfer.SellerId != command.SellerId
                || transfer.BuyerId != command.BuyerId)
            {
                throw new InventoryInvariantViolationException(
                    $"Reverse command payload does not match ItemTransfer for trade {command.TradeId}.");
            }

            if (transfer.Status == ItemTransferStatus.Finalized)
            {
                throw new InventoryInvariantViolationException(
                    $"Cannot reverse trade {command.TradeId} because it has already been finalized.");
            }

            var now = DateTimeOffset.UtcNow;

            if (transfer.Status == ItemTransferStatus.Reversed)
            {
                return new ItemTransferReversedEvent(
                    command.CorrelationId,
                    command.OperationId,
                    now,
                    command.TradeId,
                    command.ListingId,
                    command.ItemId);
            }

            if (sellerInventory is null || buyerInventory is null)
            {
                throw new InventoryInvariantViolationException(
                    $"Seller or buyer inventory was not found for trade {command.TradeId}.");
            }

            var item = await _dbContext.InventoryItems
                .FirstOrDefaultAsync(x => x.ItemId == command.ItemId, ct);

            if (item is null
                || item.OwnerId != command.BuyerId
                || item.Status != InventoryItemStatus.PendingSettlement
                || item.TradeId != command.TradeId)
            {
                throw new InventoryInvariantViolationException(
                    $"Item {command.ItemId} is not in PendingSettlement state matching trade {command.TradeId}.");
            }

            var itemLock = await _dbContext.ItemLocks
                .FirstOrDefaultAsync(x => x.ListingId == command.ListingId, ct);

            if (itemLock is null
                || itemLock.ItemId != command.ItemId
                || itemLock.SellerId != command.SellerId
                || itemLock.Status != ItemLockStatusType.Locked)
            {
                throw new InventoryInvariantViolationException(
                    $"ItemLock for listing {command.ListingId} must remain Locked to reverse trade {command.TradeId}.");
            }

            item.OwnerId = command.SellerId;
            item.Status = InventoryItemStatus.Locked;
            item.ListingId = command.ListingId;
            item.TradeId = null;
            item.Version++;

            sellerInventory.ReservedSlots = Math.Max(0, sellerInventory.ReservedSlots - 1);
            sellerInventory.Version++;

            buyerInventory.Version++;

            transfer.Status = ItemTransferStatus.Reversed;
            transfer.UpdatedAt = now;

            await _dbContext.SaveChangesAsync(ct);

            return new ItemTransferReversedEvent(
                command.CorrelationId,
                command.OperationId,
                now,
                command.TradeId,
                command.ListingId,
                command.ItemId);
        }, cancellationToken);
    }

    public Task<ItemUnlockedEvent> HandleUnlockItemAsync(
        UnlockItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateUnlockCommand(command);

        return ExecuteInLocalTransactionAsync(async ct =>
        {
            await LockInventoryRowAsync(command.SellerId, ct);

            var now = DateTimeOffset.UtcNow;

            var existingLock = await _dbContext.ItemLocks
                .FirstOrDefaultAsync(x => x.ListingId == command.ListingId, ct);

            if (existingLock is not null)
            {
                if (existingLock.ItemId != command.ItemId || existingLock.SellerId != command.SellerId)
                {
                    throw new InventoryInvariantViolationException(
                        $"ItemLock for listing {command.ListingId} does not match unlock command.");
                }

                if (existingLock.Status == ItemLockStatusType.Released)
                {
                    return new ItemUnlockedEvent(
                        command.CorrelationId,
                        command.OperationId,
                        now,
                        command.ListingId,
                        command.ItemId,
                        command.SellerId);
                }

                var lockedItem = await _dbContext.InventoryItems
                    .FirstOrDefaultAsync(x => x.ItemId == existingLock.ItemId, ct);

                if (lockedItem is null)
                {
                    throw new InventoryInvariantViolationException(
                        $"Locked item {existingLock.ItemId} for listing {command.ListingId} was not found.");
                }

                if (lockedItem.Status == InventoryItemStatus.PendingSettlement)
                {
                    throw new InventoryInvariantViolationException(
                        $"Item {lockedItem.ItemId} is in PendingSettlement and cannot be unlocked.");
                }

                if (lockedItem.OwnerId != command.SellerId)
                {
                    throw new InventoryInvariantViolationException(
                        $"Item {lockedItem.ItemId} owner does not match seller {command.SellerId}.");
                }

                if (lockedItem.Status == InventoryItemStatus.Locked)
                {
                    if (lockedItem.ListingId != command.ListingId)
                    {
                        throw new InventoryInvariantViolationException(
                            $"Item {lockedItem.ItemId} is locked by a different listing.");
                    }

                    lockedItem.Status = InventoryItemStatus.Available;
                    lockedItem.ListingId = null;
                    lockedItem.TradeId = null;
                    lockedItem.Version++;
                }

                existingLock.Status = ItemLockStatusType.Released;
                existingLock.UpdatedAt = now;

                await _dbContext.SaveChangesAsync(ct);

                return new ItemUnlockedEvent(
                    command.CorrelationId,
                    command.OperationId,
                    now,
                    command.ListingId,
                    command.ItemId,
                    command.SellerId);
            }

            var targetItem = await _dbContext.InventoryItems
                .FirstOrDefaultAsync(x => x.ItemId == command.ItemId, ct);

            if (targetItem is null)
            {
                throw new InventoryInvariantViolationException(
                    $"Cannot create unlock tombstone for listing {command.ListingId} because item {command.ItemId} does not exist.");
            }

            if (targetItem.Status == InventoryItemStatus.PendingSettlement)
            {
                throw new InventoryInvariantViolationException(
                    $"Item {targetItem.ItemId} is in PendingSettlement and cannot be unlocked.");
            }

            if (targetItem.OwnerId != command.SellerId)
            {
                throw new InventoryInvariantViolationException(
                    $"Item {targetItem.ItemId} is not owned by seller {command.SellerId}.");
            }

            if (targetItem.Status == InventoryItemStatus.Locked && targetItem.ListingId == command.ListingId)
            {
                targetItem.Status = InventoryItemStatus.Available;
                targetItem.ListingId = null;
                targetItem.TradeId = null;
                targetItem.Version++;
            }

            var tombstone = new ItemLock
            {
                ListingId = command.ListingId,
                ItemId = targetItem.ItemId,
                SellerId = command.SellerId,
                TemplateId = targetItem.TemplateId,
                Quantity = targetItem.Quantity,
                Status = ItemLockStatusType.Released,
                LockedAt = now,
                UpdatedAt = now
            };

            _dbContext.ItemLocks.Add(tombstone);
            await _dbContext.SaveChangesAsync(ct);

            return new ItemUnlockedEvent(
                command.CorrelationId,
                command.OperationId,
                now,
                command.ListingId,
                command.ItemId,
                command.SellerId);
        }, cancellationToken);
    }

    private async Task<TransferItemSagaResult?> TryGetTransferReplayAsync(
        TransferItemCommand command,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        var transferQuery = asNoTracking
            ? _dbContext.ItemTransfers.AsNoTracking()
            : _dbContext.ItemTransfers.AsQueryable();

        var existingTransfer = await transferQuery
            .FirstOrDefaultAsync(x => x.TradeId == command.TradeId, cancellationToken);

        if (existingTransfer is not null)
        {
            if (existingTransfer.Status == ItemTransferStatus.Reversed)
            {
                throw new InventoryInvariantViolationException(
                    $"Cannot replay transfer for trade {command.TradeId} because it has already been reversed.");
            }

            if (existingTransfer.ItemId != command.ItemId
                || existingTransfer.SellerId != command.SellerId
                || existingTransfer.BuyerId != command.BuyerId
                || existingTransfer.Quantity != command.Quantity)
            {
                throw new InventoryInvariantViolationException(
                    $"Transfer replay payload mismatch for trade {command.TradeId}.");
            }

            return TransferItemSagaResult.Succeeded(new ItemTransferredEvent(
                command.CorrelationId,
                command.OperationId,
                DateTimeOffset.UtcNow,
                command.TradeId,
                command.ListingId,
                existingTransfer.ItemId,
                existingTransfer.BuyerId,
                existingTransfer.Quantity));
        }

        var rejectionQuery = asNoTracking
            ? _dbContext.InventoryOperationRejections.AsNoTracking()
            : _dbContext.InventoryOperationRejections.AsQueryable();

        var existingRejection = await rejectionQuery
            .FirstOrDefaultAsync(x => x.OperationId == command.OperationId, cancellationToken);

        if (existingRejection is not null)
        {
            if (existingRejection.TradeId != command.TradeId
                || !string.Equals(existingRejection.CommandType, nameof(TransferItemCommand), StringComparison.Ordinal))
            {
                throw new InventoryInvariantViolationException(
                    $"OperationId {command.OperationId} was already rejected for a different trade or command.");
            }

            return TransferItemSagaResult.Failed(new ItemTransferFailedEvent(
                command.CorrelationId,
                command.OperationId,
                DateTimeOffset.UtcNow,
                command.TradeId,
                existingRejection.FailureCode));
        }

        return null;
    }

    private async Task<TransferItemSagaResult> PersistTransferRejectionAsync(
        TransferItemCommand command,
        string failureCode,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rejection = new InventoryOperationRejection
        {
            OperationId = command.OperationId,
            TradeId = command.TradeId,
            CommandType = nameof(TransferItemCommand),
            FailureCode = failureCode,
            CreatedAt = now
        };

        _dbContext.InventoryOperationRejections.Add(rejection);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return TransferItemSagaResult.Failed(new ItemTransferFailedEvent(
            command.CorrelationId,
            command.OperationId,
            now,
            command.TradeId,
            failureCode));
    }

    private async Task<Domain.Entities.Inventory?> EnsureAndLockInventoryRowAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsSqlServer())
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM [Inventories] WITH (UPDLOCK, HOLDLOCK) WHERE [UserId] = {0})
                BEGIN
                    INSERT INTO [Inventories] ([UserId], [Capacity], [ReservedSlots], [Version])
                    VALUES ({0}, {1}, 0, 1);
                END
                """,
                [userId, _options.DefaultCapacity],
                cancellationToken);

            return await LockInventoryRowAsync(userId, cancellationToken);
        }

        var existing = await _dbContext.Inventories
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);

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
        await _dbContext.SaveChangesAsync(cancellationToken);
        return created;
    }

    private async Task<Domain.Entities.Inventory?> LockInventoryRowAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsSqlServer())
        {
            return await _dbContext.Inventories
                .FromSqlRaw(
                    "SELECT [UserId], [Capacity], [ReservedSlots], [Version] FROM [Inventories] WITH (UPDLOCK, ROWLOCK) WHERE [UserId] = {0}",
                    userId)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return await _dbContext.Inventories
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
    }

    private async Task<T> ExecuteInLocalTransactionAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.CurrentTransaction is not null || !_dbContext.Database.IsSqlServer())
        {
            return await action(cancellationToken);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var result = await action(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static void ValidateTransferCommand(TransferItemCommand command)
    {
        if (command.OperationId == Guid.Empty
            || command.TradeId == Guid.Empty
            || command.ListingId == Guid.Empty
            || command.ItemId == Guid.Empty
            || command.TemplateId == Guid.Empty
            || command.SellerId == Guid.Empty
            || command.BuyerId == Guid.Empty
            || command.Quantity < 1)
        {
            throw new InventoryInvariantViolationException("TransferItemCommand contains invalid identifiers or quantity.");
        }

        if (command.SellerId == command.BuyerId)
        {
            throw new InventoryInvariantViolationException("SellerId and BuyerId cannot be the same player.");
        }
    }

    private static void ValidateFinalizeCommand(FinalizeItemTransferCommand command)
    {
        if (command.OperationId == Guid.Empty
            || command.TradeId == Guid.Empty
            || command.ListingId == Guid.Empty
            || command.ItemId == Guid.Empty
            || command.BuyerId == Guid.Empty)
        {
            throw new InventoryInvariantViolationException("FinalizeItemTransferCommand contains invalid identifiers.");
        }
    }

    private static void ValidateReverseCommand(ReverseItemTransferCommand command)
    {
        if (command.OperationId == Guid.Empty
            || command.TradeId == Guid.Empty
            || command.ListingId == Guid.Empty
            || command.ItemId == Guid.Empty
            || command.SellerId == Guid.Empty
            || command.BuyerId == Guid.Empty)
        {
            throw new InventoryInvariantViolationException("ReverseItemTransferCommand contains invalid identifiers.");
        }

        if (command.SellerId == command.BuyerId)
        {
            throw new InventoryInvariantViolationException("SellerId and BuyerId cannot be the same player.");
        }
    }

    private static void ValidateUnlockCommand(UnlockItemCommand command)
    {
        if (command.OperationId == Guid.Empty
            || command.ListingId == Guid.Empty
            || command.ItemId == Guid.Empty
            || command.SellerId == Guid.Empty)
        {
            throw new InventoryInvariantViolationException("UnlockItemCommand contains invalid identifiers.");
        }
    }
}