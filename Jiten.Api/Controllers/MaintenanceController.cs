using Jiten.Api.Dtos.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StackExchange.Redis;

namespace Jiten.Api.Controllers;

[ApiController]
[Route("api/maintenance")]
[ApiExplorerSettings(IgnoreApi = true)]
[EnableRateLimiting("fixed")]
public class MaintenanceController(
    IConnectionMultiplexer redis,
    ILogger<MaintenanceController> logger) : ControllerBase
{
    private const string ActiveKey = "maintenance:active";
    private const string MessageKey = "maintenance:message";
    private const string UpdatedAtKey = "maintenance:updated_at";

    [HttpGet("banner")]
    [ResponseCache(Duration = 30)]
    public async Task<IResult> GetBanner()
    {
        var db = redis.GetDatabase();
        var active = await db.StringGetAsync(ActiveKey);

        if (!active.HasValue || active != "true")
            return Results.Ok(new { isActive = false, message = (string?)null, updatedAt = (string?)null });

        var message = await db.StringGetAsync(MessageKey);
        var updatedAt = await db.StringGetAsync(UpdatedAtKey);

        return Results.Ok(new
        {
            isActive = true,
            message = message.HasValue ? message.ToString() : null,
            updatedAt = updatedAt.HasValue ? updatedAt.ToString() : null
        });
    }

    [HttpPost("banner")]
    [Authorize("RequiresAdmin")]
    public async Task<IResult> SetBanner([FromBody] SetMaintenanceBannerRequest request)
    {
        var db = redis.GetDatabase();
        var updatedAt = DateTime.UtcNow.ToString("O");

        await db.StringSetAsync(ActiveKey, request.IsActive ? "true" : "false");
        await db.StringSetAsync(MessageKey, request.Message ?? "");
        await db.StringSetAsync(UpdatedAtKey, updatedAt);

        logger.LogInformation(
            "Admin set maintenance banner: Active={Active}, Message={Message}",
            request.IsActive, request.Message);

        return Results.Ok(new { message = "Maintenance banner updated" });
    }
}
