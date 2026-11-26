using Admin.API.Application.Common;
using Admin.API.Application.DTOs;
using Admin.API.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Admin.API.Controllers;

[ApiController]
[Route("api/v1/admin/analytics")]
[Authorize(Roles = "GM")]
public sealed class AdminAnalyticsController : ControllerBase {
    private readonly IMarketAnalyticsService _analyticsService;

    public AdminAnalyticsController(IMarketAnalyticsService analyticsService) {
        _analyticsService = analyticsService;
    }

    [HttpGet]
    public async Task<IActionResult> GetOverview(
        [FromQuery] DateOnly? day = null,
        CancellationToken cancellationToken = default
    ) {
        var result = await _analyticsService.GetOverviewAsync(day, HttpContext.TraceIdentifier, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToProblemResult(result.Error!);
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] Guid? templateId = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default
    ) {
        var result = await _analyticsService.GetHistoryAsync(templateId, from, to, page, pageSize, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToProblemResult(result.Error!);
    }

    private ObjectResult ToProblemResult(AdminError error) {
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
