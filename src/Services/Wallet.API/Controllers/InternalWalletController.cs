using Microsoft.AspNetCore.Mvc;
using Wallet.API.Application.DTOs;
using Wallet.API.Application.Services;
using Wallet.API.Infrastructure.Security;

namespace Wallet.API.Controllers;

[ApiController]
[Route("internal/v1/wallet")]
public sealed class InternalWalletController : ControllerBase
{
    private readonly IWalletQueryService _walletQueryService;

    public InternalWalletController(IWalletQueryService walletQueryService)
    {
        _walletQueryService = walletQueryService;
    }

    [HttpGet("trades/{tradeId}/trace")]
    [RequireServiceCaller("admin", "marketplace")]
    public async Task<ActionResult<WalletTradeTraceResponse>> GetTradeTrace(
        Guid tradeId,
        CancellationToken cancellationToken)
    {
        var trace = await _walletQueryService.GetTradeTraceAsync(tradeId, cancellationToken);
        return Ok(trace);
    }

    [HttpGet("supply")]
    [RequireServiceCaller("admin")]
    public async Task<ActionResult<WalletSupplyResponse>> GetSupply(CancellationToken cancellationToken)
    {
        var supply = await _walletQueryService.GetSupplyAsync(cancellationToken);
        return Ok(supply);
    }
}