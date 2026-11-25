using Gateway.API.Application.DTOs;
using Gateway.API.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.API.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase {
    private readonly IAuthStatusClient _authStatusClient;

    public HealthController(IAuthStatusClient authStatusClient) {
        _authStatusClient = authStatusClient;
    }

    [HttpGet("live")]
    public IActionResult Live() {
        return Ok(new HealthDto("Healthy"));
    }

    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken) {
        var authReady = await _authStatusClient.IsAuthReadyAsync(cancellationToken);
        return authReady
            ? Ok(new HealthDto("Healthy"))
            : StatusCode(StatusCodes.Status503ServiceUnavailable, new HealthDto("Unhealthy"));
    }
}
