using System.Security.Claims;
using Marketplace.API.Application.Common;
using Marketplace.API.Application.DTOs;
using Marketplace.API.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.API.Controllers;

[ApiController]
[Route("api/v1/marketplace")]
[Authorize(Roles = "Player")]
public sealed class TradesController : ControllerBase
{
    private readonly ITradeQueryService _tradeQueryService;

    public TradesController(ITradeQueryService tradeQueryService)
    {
        _tradeQueryService = tradeQueryService;
    }

    [HttpGet("history")]
    public async Task<ActionResult<TradePageDto>> GetHistory(
        [FromQuery] string? side = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var userId = GetAuthenticatedUserId();
        var result = await _tradeQueryService.GetHistoryAsync(userId, side, status, page, pageSize, from, to, ct);
        return Ok(result);
    }

    [HttpGet("trades/{tradeId:guid}")]
    public async Task<ActionResult<TradeDto>> GetTrade(
        [FromRoute] Guid tradeId,
        CancellationToken ct)
    {
        var userId = GetAuthenticatedUserId();
        var result = await _tradeQueryService.GetTradeAsync(userId, tradeId, ct);
        return Ok(result);
    }

    private Guid GetAuthenticatedUserId()
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(sub, out var userId))
            throw new MarketplaceApiException(401, MarketplaceErrorCodes.Unauthenticated, "Missing or invalid user subject claim.");

        return userId;
    }
}
