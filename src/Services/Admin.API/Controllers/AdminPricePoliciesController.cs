using System.Security.Claims;
using Admin.API.Application.Common;
using Admin.API.Application.DTOs;
using Admin.API.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Admin.API.Controllers;

[ApiController]
[Route("api/v1/admin/price-policies")]
[Authorize(Roles = "GM")]
public sealed class AdminPricePoliciesController : ControllerBase {
    private readonly IPricePolicyService _pricePolicyService;

    public AdminPricePoliciesController(IPricePolicyService pricePolicyService) {
        _pricePolicyService = pricePolicyService;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default
    ) {
        var result = await _pricePolicyService.ListAsync(page, pageSize, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToProblemResult(result.Error!);
    }

    [HttpPut("{templateId:guid}")]
    public async Task<IActionResult> Upsert(
        [FromRoute] Guid templateId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] PricePolicyRequest request,
        CancellationToken cancellationToken = default
    ) {
        var sub = User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var actorId) || actorId == Guid.Empty) {
            return ToProblemResult(AdminErrors.Unauthenticated("Authenticated token is missing a valid user subject."));
        }

        var result = await _pricePolicyService.UpsertAsync(
            templateId,
            actorId,
            idempotencyKey ?? string.Empty,
            request,
            cancellationToken
        );

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
