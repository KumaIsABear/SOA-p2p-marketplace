using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Wallet.API.Domain.Errors;
using Wallet.API.Infrastructure.Http;
using Wallet.API.Infrastructure.Options;

namespace Wallet.API.Infrastructure.Security;

public sealed class ServiceKeyAuthorizationFilter : IAsyncAuthorizationFilter
{
    public const string HeaderName = "X-Service-Key";
    public const string ServiceCallerItemKey = "ServiceCaller";

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
            context.Result = CreateProblemResult(
                context.HttpContext,
                StatusCodes.Status401Unauthorized,
                WalletErrorCodes.Unauthenticated,
                "Missing internal service authentication key.");
            return Task.CompletedTask;
        }

        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));
        string? matchedCaller = null;

        foreach (var (callerName, callerConfig) in _options.AllowedCallers)
        {
            if (string.IsNullOrWhiteSpace(callerConfig.Key))
            {
                continue;
            }

            var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(callerConfig.Key));
            if (CryptographicOperations.FixedTimeEquals(providedHash, expectedHash))
            {
                matchedCaller = callerName;
                break;
            }
        }

        if (matchedCaller is null)
        {
            context.Result = CreateProblemResult(
                context.HttpContext,
                StatusCodes.Status401Unauthorized,
                WalletErrorCodes.Unauthenticated,
                "Invalid internal service authentication key.");
            return Task.CompletedTask;
        }

        var isAllowed = _allowedCallers.Any(
            c => string.Equals(c, matchedCaller, StringComparison.OrdinalIgnoreCase));

        if (!isAllowed)
        {
            context.Result = CreateProblemResult(
                context.HttpContext,
                StatusCodes.Status403Forbidden,
                WalletErrorCodes.Forbidden,
                "Service caller is not permitted to access this endpoint.");
            return Task.CompletedTask;
        }

        context.HttpContext.Items[ServiceCallerItemKey] = matchedCaller.ToLowerInvariant();
        return Task.CompletedTask;
    }

    private static IActionResult CreateProblemResult(
        HttpContext httpContext,
        int statusCode,
        string code,
        string detail)
    {
        var problem = ProblemDetailsHelper.Create(httpContext, statusCode, code, detail);
        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
    }
}