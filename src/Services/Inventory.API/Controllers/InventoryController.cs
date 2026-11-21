using System.Security.Claims;
using Inventory.API.Application.DTOs;
using Inventory.API.Application.Exceptions;
using Inventory.API.Application.Services;
using Inventory.API.Domain.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.API.Controllers;

[ApiController]
[Route("api/v1/inventory")]
public sealed class InventoryController : ControllerBase
{
    private readonly IInventoryService _inventoryService;

    public InventoryController(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    [HttpGet("items")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult<InventoryViewDto>> GetMyInventory(CancellationToken cancellationToken)
    {
        var sub = User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId) || userId == Guid.Empty)
        {
            throw new InventoryApiException(
                StatusCodes.Status401Unauthorized,
                InventoryErrorCodes.Unauthenticated,
                "Authenticated token is missing a valid user subject.");
        }

        var view = await _inventoryService.GetPlayerInventoryAsync(userId, cancellationToken);
        return Ok(view);
    }
}