using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Wallet.API.Infrastructure.Persistence;

namespace Wallet.API.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    private readonly WalletDbContext _dbContext;
    private readonly IBusControl _busControl;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        WalletDbContext dbContext,
        IBusControl busControl,
        ILogger<HealthController> logger)
    {
        _dbContext = dbContext;
        _busControl = busControl;
        _logger = logger;
    }

    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "Healthy" });

    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken)
    {
        try
        {
            var canConnectDb = await _dbContext.Database.CanConnectAsync(cancellationToken);
            var busHealth = _busControl.CheckHealth();

            if (!canConnectDb || busHealth.Status != BusHealthStatus.Healthy)
            {
                _logger.LogWarning(
                    "Wallet readiness check failed. DbCanConnect={DbCanConnect}, BusStatus={BusStatus}, BusDescription={BusDescription}",
                    canConnectDb,
                    busHealth.Status,
                    busHealth.Description);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "Unhealthy" });
            }

            return Ok(new { status = "Healthy" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Wallet readiness check threw an exception.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "Unhealthy" });
        }
    }
}