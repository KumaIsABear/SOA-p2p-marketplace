using Admin.API.Application.Common;
using Admin.API.Application.DTOs;
using Admin.API.Application.Interfaces;
using Admin.API.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;

namespace Admin.API.Controllers;

[ApiController]
[Route("internal/v1/admin")]
public sealed class InternalAdminController : ControllerBase {
    private readonly IPricePolicyService _pricePolicyService;

    public InternalAdminController(IPricePolicyService pricePolicyService) {
        _pricePolicyService = pricePolicyService;
    }

    [HttpGet("price-policies/{templateId:guid}")]
    [RequireServiceCaller("marketplace")]
    public async Task<IActionResult> GetPricePolicy(
        [FromRoute] Guid templateId,
        CancellationToken cancellationToken = default
    ) {
        var result = await _pricePolicyService.GetAndRefreshAsync(templateId, cancellationToken);
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
