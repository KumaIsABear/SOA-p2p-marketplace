using Marketplace.API.Application.DTOs;

namespace Marketplace.API.Application.Interfaces;

public interface IInventoryClient
{
    Task<InventoryItemDto?> GetItemAsync(Guid itemId, CancellationToken ct);
    Task<LockCallResult> LockItemAsync(LockItemRequestDto request, CancellationToken ct);
    Task<bool> TryUnlockItemAsync(Guid listingId, UnlockItemRequestDto request, CancellationToken ct);
    Task<ItemLockStatusDto?> GetLockStatusAsync(Guid listingId, CancellationToken ct);
    Task<ItemTransferTraceDto?> GetTransferTraceAsync(Guid tradeId, CancellationToken ct);
}
