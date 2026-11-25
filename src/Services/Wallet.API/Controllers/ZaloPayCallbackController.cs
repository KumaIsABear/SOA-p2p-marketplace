using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wallet.API.Application.DTOs;
using Wallet.API.Application.Services;

namespace Wallet.API.Controllers;

[ApiController]
[Route("api/v1/wallet/zalopay")]
[AllowAnonymous]
public sealed class ZaloPayCallbackController : ControllerBase
{
    private readonly IWalletTopUpService _walletTopUpService;

    public ZaloPayCallbackController(IWalletTopUpService walletTopUpService)
    {
        _walletTopUpService = walletTopUpService;
    }

    [HttpPost("callback")]
    public async Task<ActionResult<ZaloPayCallbackResponse>> Callback(
        [FromBody] ZaloPayCallbackRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _walletTopUpService.HandleZaloPayCallbackAsync(request, cancellationToken);
        return Ok(response);
    }
}