using Marketplace.API.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace Marketplace.API.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    private readonly MarketplaceDbContext _db;
    private readonly IConnectionMultiplexer _redis;

    public HealthController(MarketplaceDbContext db, IConnectionMultiplexer redis)
    {
        _db = db;
        _redis = redis;
    }

    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "Healthy" });

    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken ct)
    {
        try
        {
            var sqlOk = await _db.Database.CanConnectAsync(ct);
            await _redis.GetDatabase().PingAsync();

            return sqlOk
                ? Ok(new { status = "Healthy" })
                : StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "Unhealthy" });
        }
        catch
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "Unhealthy" });
        }
    }
}
