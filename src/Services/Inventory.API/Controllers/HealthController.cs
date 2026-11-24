using Inventory.API.Infrastructure.Persistence;
using MassTransit;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.API.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    private readonly InventoryDbContext _dbContext;
    private readonly IBusControl _busControl;

    public HealthController(InventoryDbContext dbContext, IBusControl busControl)
    {
        _dbContext = dbContext;
        _busControl = busControl;
    }

    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "Healthy" });

    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken)
    {
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            var busHealth = _busControl.CheckHealth();

            if (!canConnect || busHealth.Status != BusHealthStatus.Healthy)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "Unhealthy" });
            }

            return Ok(new { status = "Healthy" });
        }
        catch
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "Unhealthy" });
        }
    }
}