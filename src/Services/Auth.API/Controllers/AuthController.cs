using System.Security.Claims;
using Auth.API.Application.Common;
using Auth.API.Application.DTOs;
using Auth.API.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase {
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService) {
        _authService = authService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken) {
        var result = await _authService.LoginAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToProblemResult(result.Error!);
    }

    [HttpPost("google")]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request, CancellationToken cancellationToken) {
        var result = await _authService.GoogleLoginAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToProblemResult(result.Error!);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken cancellationToken) {
        var result = await _authService.RefreshAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToProblemResult(result.Error!);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest request, CancellationToken cancellationToken) {
        await _authService.LogoutAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize(Roles = "Player,GM,CS")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken) {
        var sub = User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId) || userId == Guid.Empty) {
            return ToProblemResult(AuthErrors.Unauthenticated("Authenticated token is missing a valid user subject."));
        }

        var result = await _authService.GetCurrentUserAsync(userId, cancellationToken);
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
