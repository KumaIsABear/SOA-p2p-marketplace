using System.Security.Claims;
using Admin.API.Application.Common;
using Admin.API.Application.DTOs;
using Admin.API.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Admin.API.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "CS")]
public sealed class AdminSupportController : ControllerBase {
    private readonly ISupportService _supportService;

    public AdminSupportController(ISupportService supportService) {
        _supportService = supportService;
    }

    [HttpGet("trades/{tradeId:guid}/trace")]
    public async Task<IActionResult> GetTradeTrace(
        [FromRoute] Guid tradeId,
        CancellationToken cancellationToken = default
    ) {
        var result = await _supportService.GetTradeTraceAsync(tradeId, HttpContext.TraceIdentifier, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToProblemResult(result.Error!);
    }

    [HttpGet("trades")]
    public async Task<IActionResult> SearchTrades(
        [FromQuery] Guid? userId = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default
    ) {
        var result = await _supportService.SearchTradesAsync(
            userId,
            status,
            page,
            pageSize,
            from,
            to,
            HttpContext.TraceIdentifier,
            cancellationToken
        );
        return result.IsSuccess ? Ok(result.Value) : ToProblemResult(result.Error!);
    }

    [HttpPatch("users/{userId:guid}/status")]
    public async Task<IActionResult> ModerateUser(
        [FromRoute] Guid userId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] ModerationRequest request,
        CancellationToken cancellationToken = default
    ) {
        var sub = User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var actorId) || actorId == Guid.Empty) {
            return ToProblemResult(AdminErrors.Unauthenticated("Authenticated token is missing a valid user subject."));
        }

        var result = await _supportService.ModerateUserAsync(
            userId,
            actorId,
            idempotencyKey ?? string.Empty,
            request,
            HttpContext.TraceIdentifier,
            cancellationToken
        );

        return result.IsSuccess ? Ok(result.Value) : ToProblemResult(result.Error!);
    }

    private ObjectResult ToProblemResult(AdminError error) {
        var problem = new ApiProblemDetails(
            "about:blank",
            error.Title,
            error.StatusCode,
            error.Detail,
            HttpContext.Request.Path.Value ?? "/",
            error.Code,
            HttpContext.TraceIdentifier,
            error.Errors
        );

        return new ObjectResult(problem) {
            StatusCode = error.StatusCode,
            ContentTypes = { "application/problem+json" }
        };
    }
}
