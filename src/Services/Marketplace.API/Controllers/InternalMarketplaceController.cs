using Marketplace.API.Application.DTOs;
using Marketplace.API.Application.Interfaces;
using Marketplace.API.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.API.Controllers;

[ApiController]
[Route("internal/v1/marketplace")]
[RequireServiceCaller("admin")]
public sealed class InternalMarketplaceController : ControllerBase
{
    private readonly IInternalMarketplaceService _service;

    public InternalMarketplaceController(IInternalMarketplaceService service)
    {
        _service = service;
    }

    [HttpGet("trades/{tradeId:guid}/trace")]
    public async Task<ActionResult<MarketTradeTraceDto>> GetTradeTrace(
        [FromRoute] Guid tradeId,
        CancellationToken ct)
    {
        var result = await _service.GetTradeTraceAsync(tradeId, ct);
        return Ok(result);
    }

    [HttpGet("trades")]
    public async Task<ActionResult<TradePageDto>> SearchTrades(
        [FromQuery] Guid userId,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var result = await _service.SearchTradesByUserAsync(userId, status, page, pageSize, from, to, ct);
        return Ok(result);
    }

    [HttpGet("stats")]
    public async Task<ActionResult<MarketListingStatsDto>> GetStats(CancellationToken ct)
    {
        var result = await _service.GetStatsAsync(ct);
        return Ok(result);
    }
}
