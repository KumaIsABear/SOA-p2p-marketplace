using Gateway.API.Application.Options;
using Gateway.API.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace Gateway.API.Infrastructure.Middleware;

public sealed class TechnicalEndpointGuardMiddleware {
    private static readonly string[] ForwardedIndicators = [
        "X-Forwarded-For",
        "X-Forwarded-Host",
        "X-Original-Host",
        "Forwarded"
    ];

    private readonly RequestDelegate _next;
    private readonly HashSet<string> _internalHosts;

    public TechnicalEndpointGuardMiddleware(RequestDelegate next, IOptions<GatewayOptions> options) {
        _next = next;
        _internalHosts = new HashSet<string>(options.Value.InternalHosts, StringComparer.OrdinalIgnoreCase);
    }

    public async Task InvokeAsync(HttpContext context) {
        var path = context.Request.Path;

        if (IsAlwaysBlockedPath(path)) {
            await ProblemResponseWriter.WriteAsync(
                context,
                StatusCodes.Status404NotFound,
                "Not Found",
                "ROUTE_NOT_FOUND",
                "The requested endpoint does not exist."
            );
            return;
        }

        if (IsInternalTechnicalPath(path) && !IsInternalOrigin(context)) {
            await ProblemResponseWriter.WriteAsync(
                context,
                StatusCodes.Status404NotFound,
                "Not Found",
                "ROUTE_NOT_FOUND",
                "The requested endpoint does not exist."
            );
            return;
        }

        await _next(context);
    }

    private static bool IsAlwaysBlockedPath(PathString path) {
        return path.StartsWithSegments("/internal", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInternalTechnicalPath(PathString path) {
        return path.StartsWithSegments("/metrics", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsInternalOrigin(HttpContext context) {
        foreach (var header in ForwardedIndicators) {
            if (context.Request.Headers.ContainsKey(header)) {
                return false;
            }
        }

        var host = context.Request.Host.Host;
        return !string.IsNullOrWhiteSpace(host) && _internalHosts.Contains(host);
    }
}
