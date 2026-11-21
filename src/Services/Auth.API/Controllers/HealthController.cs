using Auth.API.Application.DTOs;
using Auth.API.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace Auth.API.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase {
    private readonly AuthDbContext _db;

    public HealthController(AuthDbContext db) {
        _db = db;
    }

    [HttpGet("live")]
    public IActionResult Live() {
        return Ok(new HealthDto("Healthy"));
    }

    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken) {
        try {
            var canConnect = await _db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? Ok(new HealthDto("Healthy"))
                : StatusCode(StatusCodes.Status503ServiceUnavailable, new HealthDto("Unhealthy"));
        } catch {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new HealthDto("Unhealthy"));
        }
    }
}
