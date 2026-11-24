using System.Security.Cryptography;
using System.Text;
using Marketplace.API.Application.Common;
using Marketplace.API.Application.DTOs;
using Marketplace.API.Application.Options;
using Marketplace.API.Infrastructure.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Marketplace.API.Infrastructure.Security;

public sealed class ServiceKeyAuthorizationFilter : IAsyncAuthorizationFilter
{
    public const string HeaderName = "X-Service-Key";

    private readonly string[] _allowedCallers;
    private readonly ServiceAuthOptions _options;

    public ServiceKeyAuthorizationFilter(string[] allowedCallers, IOptions<ServiceAuthOptions> options)
    {
        _allowedCallers = allowedCallers;
        _options = options.Value;
    }

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var providedKey = context.HttpContext.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(providedKey))
        {
            context.Result = CreateProblem(context.HttpContext, 401, "Unauthorized", MarketplaceErrorCodes.Unauthenticated, "Missing internal service authentication key.");
            return Task.CompletedTask;
        }

        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));
        string? matchedCaller = null;

        foreach (var (callerName, callerConfig) in _options.AllowedCallers)
        {
            if (string.IsNullOrWhiteSpace(callerConfig.Key))
                continue;

            var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(callerConfig.Key));
            if (CryptographicOperations.FixedTimeEquals(providedHash, expectedHash) && matchedCaller is null)
                matchedCaller = callerName;
        }

        if (matchedCaller is null)
        {
            context.Result = CreateProblem(context.HttpContext, 401, "Unauthorized", MarketplaceErrorCodes.Unauthenticated, "Invalid internal service authentication key.");
            return Task.CompletedTask;
        }

        if (!_allowedCallers.Any(c => string.Equals(c, matchedCaller, StringComparison.OrdinalIgnoreCase)))
        {
            context.Result = CreateProblem(context.HttpContext, 403, "Forbidden", MarketplaceErrorCodes.Forbidden, "Service caller is not permitted to access this endpoint.");
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }

    private static IActionResult CreateProblem(
        HttpContext httpContext,
        int statusCode,
        string title,
        string code,
        string detail)
    {
        var traceId = httpContext.Items[CorrelationIdMiddleware.ItemKey]?.ToString() ?? httpContext.TraceIdentifier;
        var problem = new ApiProblemDetails(
            "about:blank",
            title,
            statusCode,
            detail,
            httpContext.Request.Path.Value ?? "/",
            code,
            traceId
        );

        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
    }
}
