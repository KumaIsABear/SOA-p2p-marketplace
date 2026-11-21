using Inventory.API.Application.DTOs;

namespace Inventory.API.Application.Services;

public interface IInventoryService
{
    Task<InventoryViewDto> GetPlayerInventoryAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<InventoryItemDto> GrantItemAsync(
        string actorId,
        string idempotencyKey,
        GrantItemRequest request,
        CancellationToken cancellationToken = default);

    Task<InventoryItemDto> GetItemAsync(Guid itemId, CancellationToken cancellationToken = default);

    Task<LockItemResultDto> LockItemAsync(LockItemRequest request, CancellationToken cancellationToken = default);

    Task<ItemLockStatusDto> UnlockItemAsync(
        Guid listingId,
        UnlockItemRequest request,
        CancellationToken cancellationToken = default);

    Task<ItemLockStatusDto> GetLockStatusAsync(Guid listingId, CancellationToken cancellationToken = default);
}