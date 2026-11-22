using Auth.API.Application.Common;
using Auth.API.Application.DTOs;
using Auth.API.Application.Interfaces;
using Auth.API.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;

namespace Auth.API.Controllers;

[ApiController]
[Route("internal/v1/auth")]
public sealed class InternalAuthController : ControllerBase {
    private readonly IAuthService _authService;

    public InternalAuthController(IAuthService authService) {
        _authService = authService;
    }

    [HttpGet("users/{userId:guid}/status")]
    [RequireServiceCaller("gateway", "marketplace", "admin")]
    public async Task<IActionResult> GetUserStatus([FromRoute] Guid userId, CancellationToken cancellationToken) {
        var result = await _authService.GetUserStatusAsync(userId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToProblemResult(result.Error!);
    }

    [HttpPatch("users/{userId:guid}/status")]
    [RequireServiceCaller("admin")]
    public async Task<IActionResult> UpdateUserStatus(
        [FromRoute] Guid userId,
        [FromBody] InternalModerationRequest request,
        CancellationToken cancellationToken
    ) {
        var result = await _authService.UpdateUserStatusAsync(userId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToProblemResult(result.Error!);
    }

    private ObjectResult ToProblemResult(AuthError error) {
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
