using System.Security.Claims;
using Marketplace.API.Application.Common;
using Marketplace.API.Application.DTOs;
using Marketplace.API.Application.Interfaces;
using Marketplace.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.API.Controllers;

[ApiController]
[Route("api/v1/marketplace")]
[Authorize(Roles = "Player")]
public sealed class ListingsController : ControllerBase
{
    private readonly IListingCommandService _service;

    public ListingsController(IListingCommandService service)
    {
        _service = service;
    }

    [HttpPost("listings")]
    public async Task<IActionResult> CreateListing(
        [FromBody] CreateListingRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var userId = GetAuthenticatedUserId();
        var outcome = await _service.CreateListingAsync(userId, idempotencyKey ?? string.Empty, request, ct);

        Response.Headers.Location = $"/api/v1/marketplace/listings/{outcome.Listing.ListingId}";
        return StatusCode(outcome.StatusCode, outcome.Listing);
    }

    [HttpDelete("listings/{listingId:guid}")]
    public async Task<ActionResult<CancelListingResultDto>> CancelListing(
        [FromRoute] Guid listingId,
        CancellationToken ct)
    {
        var userId = GetAuthenticatedUserId();
        var result = await _service.CancelListingAsync(userId, listingId, ct);
        return Ok(result);
    }

    [HttpGet("listings/{listingId:guid}")]
    public async Task<ActionResult<ListingDto>> GetListing(
        [FromRoute] Guid listingId,
        CancellationToken ct)
    {
        var userId = GetAuthenticatedUserId();
        var result = await _service.GetListingAsync(userId, listingId, ct);
        return Ok(result);
    }

    [HttpGet("my-listings")]
    public async Task<ActionResult<ListingPageDto>> GetMyListings(
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var userId = GetAuthenticatedUserId();

        var parsedStatus = ListingStatus.Active;
        if (!string.IsNullOrWhiteSpace(status) && !Enum.TryParse(status, ignoreCase: false, out parsedStatus))
        {
            throw new MarketplaceApiException(
                400,
                MarketplaceErrorCodes.ValidationError,
                $"Invalid listing status '{status}'."
            );
        }

        var result = await _service.GetMyListingsAsync(userId, parsedStatus, page, pageSize, ct);
        return Ok(result);
    }

    private Guid GetAuthenticatedUserId()
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(sub, out var userId))
            throw new MarketplaceApiException(401, MarketplaceErrorCodes.Unauthenticated, "Missing or invalid user subject claim.");

        return userId;
    }
}
