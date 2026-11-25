using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wallet.API.Application.DTOs;
using Wallet.API.Application.Exceptions;
using Wallet.API.Application.Services;
using Wallet.API.Domain.Errors;

namespace Wallet.API.Controllers;

[ApiController]
[Route("api/v1/wallet")]
[Authorize(Policy = "PlayerOnly")]
public sealed class WalletController : ControllerBase
{
    private readonly IWalletQueryService _walletQueryService;
    private readonly IWalletTopUpService _walletTopUpService;

    public WalletController(
        IWalletQueryService walletQueryService,
        IWalletTopUpService walletTopUpService)
    {
        _walletQueryService = walletQueryService;
        _walletTopUpService = walletTopUpService;
    }

    [HttpGet("balance")]
    public async Task<ActionResult<BalanceResponse>> GetBalance(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var balance = await _walletQueryService.GetOrCreateBalanceAsync(userId, cancellationToken);
        return Ok(balance);
    }

    [HttpGet("transactions")]
    public async Task<ActionResult<WalletTransactionPageResponse>> GetTransactions(
        [FromQuery] WalletTransactionQuery query,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var page = await _walletQueryService.GetTransactionsAsync(userId, query, cancellationToken);
        return Ok(page);
    }

    [HttpGet("top-up-packages")]
    public ActionResult<TopUpPackagesResponse> GetTopUpPackages()
    {
        var packages = _walletQueryService.GetTopUpPackages();
        return Ok(packages);
    }

    [HttpPost("top-ups")]
    public async Task<ActionResult<TopUpOrderResponse>> CreateTopUp(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] CreateTopUpRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var result = await _walletTopUpService.CreateTopUpAsync(
            userId,
            idempotencyKey,
            request,
            cancellationToken);

        Response.Headers.Location = result.Location;
        return StatusCode(result.StatusCode, result.Order);
    }

    [HttpGet("top-ups/{topUpId}")]
    public async Task<ActionResult<TopUpOrderResponse>> GetTopUp(
        Guid topUpId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var order = await _walletTopUpService.GetTopUpByIdAsync(userId, topUpId, cancellationToken);
        return Ok(order);
    }

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId))
        {
            throw new WalletApiException(
                StatusCodes.Status401Unauthorized,
                WalletErrorCodes.Unauthenticated,
                "The access token does not contain a valid subject claim.");
        }

        return userId;
    }
}