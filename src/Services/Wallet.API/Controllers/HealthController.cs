using Microsoft.AspNetCore.Mvc;

namespace Wallet.API.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase {
    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "Healthy" });

    [HttpGet("ready")]
    public IActionResult Ready() => Ok(new { status = "Healthy" });
}
