using System.Security.Claims;
using Gateway.API.Application.Interfaces;
using Gateway.API.Infrastructure.Http;
using Gateway.API.Infrastructure.Routing;

namespace Gateway.API.Infrastructure.Middleware;

public sealed class AccountStatusGuardMiddleware {
    private readonly RequestDelegate _next;

    public AccountStatusGuardMiddleware(RequestDelegate next) {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IAuthStatusClient authStatusClient) {
        var proxyFeature = context.GetReverseProxyFeature();
        var metadata = proxyFeature.Route.Config.Metadata;

        if (metadata is null
            || !metadata.TryGetValue(GatewayRouteMetadata.RequireAccountStatus, out var requireCheck)
            || !string.Equals(requireCheck, "true", StringComparison.OrdinalIgnoreCase)) {
            await _next(context);
            return;
        }

        var sub = context.User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId) || userId == Guid.Empty) {
            await ProblemResponseWriter.WriteAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                "UNAUTHENTICATED",
                "Authenticated token is missing a valid user subject."
            );
            return;
        }

        var lookup = await authStatusClient.GetUserStatusAsync(userId, context.TraceIdentifier, context.RequestAborted);
        if (lookup.Outcome == AuthStatusLookupOutcome.Unavailable) {
            await ProblemResponseWriter.WriteAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "Service Unavailable",
                "DEPENDENCY_UNAVAILABLE",
                "Unable to verify account status at this time."
            );
            return;
        }

        if (lookup.Outcome == AuthStatusLookupOutcome.NotFound || lookup.Status is null) {
            await ProblemResponseWriter.WriteAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                "UNAUTHENTICATED",
                "Authenticated account could not be found."
            );
            return;
        }

        var status = lookup.Status;
        if (string.Equals(status.Status, "Banned", StringComparison.OrdinalIgnoreCase)) {
            await ProblemResponseWriter.WriteAsync(
                context,
                StatusCodes.Status403Forbidden,
                "Forbidden",
                "ACCOUNT_BANNED",
                "This account is banned."
            );
            return;
        }

        if (metadata.TryGetValue(GatewayRouteMetadata.RequiredRoles, out var requiredRolesCsv)
            && !string.IsNullOrWhiteSpace(requiredRolesCsv)) {
            var allowedRoles = requiredRolesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!allowedRoles.Any(r => string.Equals(r, status.Role, StringComparison.OrdinalIgnoreCase))) {
                await ProblemResponseWriter.WriteAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "FORBIDDEN",
                    "You do not have permission to access this resource."
                );
                return;
            }
        }

        if (metadata.TryGetValue(GatewayRouteMetadata.RequireCanTrade, out var requireCanTrade)
            && string.Equals(requireCanTrade, "true", StringComparison.OrdinalIgnoreCase)) {
            if (string.Equals(status.Status, "TradeRestricted", StringComparison.OrdinalIgnoreCase) || !status.CanTrade) {
                await ProblemResponseWriter.WriteAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "TRADE_RESTRICTED",
                    "This account is restricted from creating listings or purchasing items."
                );
                return;
            }
        }

        await _next(context);
    }
}
