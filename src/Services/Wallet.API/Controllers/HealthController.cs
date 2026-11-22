using Microsoft.AspNetCore.Mvc;
using Wallet.API.Infrastructure.Persistence;

namespace Wallet.API.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    private readonly WalletDbContext _dbContext;
    private readonly ILogger<HealthController> _logger;

    public HealthController(WalletDbContext dbContext, ILogger<HealthController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "Healthy" });

    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken)
    {
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "Unhealthy" });
            }

            return Ok(new { status = "Healthy" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WalletDb readiness check failed.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "Unhealthy" });
        }
    }
}