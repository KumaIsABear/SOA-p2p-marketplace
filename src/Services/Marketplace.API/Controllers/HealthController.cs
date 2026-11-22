using Marketplace.API.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using StackExchange.Redis;

namespace Marketplace.API.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    private readonly MarketplaceDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly IMongoDatabase _mongoDatabase;

    public HealthController(
        MarketplaceDbContext db,
        IConnectionMultiplexer redis,
        IMongoDatabase mongoDatabase)
    {
        _db = db;
        _redis = redis;
        _mongoDatabase = mongoDatabase;
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
            await _mongoDatabase.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);

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
