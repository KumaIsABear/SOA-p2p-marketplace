using Inventory.API.Application.DTOs;
using Inventory.API.Application.Services;
using Inventory.API.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.API.Controllers;

[ApiController]
[Route("internal/v1/inventory")]
public sealed class InternalInventoryController : ControllerBase
{
    private readonly IInventoryService _inventoryService;

    public InternalInventoryController(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    [HttpPost("items")]
    [RequireServiceCaller("seeder")]
    public async Task<ActionResult<InventoryItemDto>> GrantItem(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] GrantItemRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = HttpContext.Items[ServiceKeyAuthorizationFilter.ServiceCallerItemKey]?.ToString() ?? "seeder";
        var created = await _inventoryService.GrantItemAsync(
            actorId,
            idempotencyKey ?? string.Empty,
            request,
            cancellationToken);

        var location = $"/internal/v1/inventory/items/{created.ItemId:D}";
        Response.Headers.Location = location;
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpGet("items/{itemId:guid}")]
    [RequireServiceCaller("marketplace")]
    public async Task<ActionResult<InventoryItemDto>> GetItem(
        [FromRoute] Guid itemId,
        CancellationToken cancellationToken)
    {
        var item = await _inventoryService.GetItemAsync(itemId, cancellationToken);
        return Ok(item);
    }

    [HttpPost("lock")]
    [RequireServiceCaller("marketplace")]
    public async Task<ActionResult<LockItemResultDto>> LockItem(
        [FromBody] LockItemRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _inventoryService.LockItemAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("locks/{listingId:guid}/unlock")]
    [RequireServiceCaller("marketplace")]
    public async Task<ActionResult<ItemLockStatusDto>> UnlockItem(
        [FromRoute] Guid listingId,
        [FromBody] UnlockItemRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _inventoryService.UnlockItemAsync(listingId, request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("locks/{listingId:guid}")]
    [RequireServiceCaller("marketplace")]
    public async Task<ActionResult<ItemLockStatusDto>> GetLockStatus(
        [FromRoute] Guid listingId,
        CancellationToken cancellationToken)
    {
        var result = await _inventoryService.GetLockStatusAsync(listingId, cancellationToken);
        return Ok(result);
    }

    [HttpGet("trades/{tradeId:guid}/trace")]
    [RequireServiceCaller("admin", "marketplace")]
    public async Task<ActionResult<ItemTransferTraceDto>> GetTradeTransferTrace(
        [FromRoute] Guid tradeId,
        CancellationToken cancellationToken)
    {
        var result = await _inventoryService.GetTradeTransferTraceAsync(tradeId, cancellationToken);
        return Ok(result);
    }
}