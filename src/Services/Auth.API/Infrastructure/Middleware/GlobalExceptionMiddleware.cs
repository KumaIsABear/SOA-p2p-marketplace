using System.Text.Json;
using Auth.API.Application.DTOs;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Auth.API.Infrastructure.Middleware;

public sealed class GlobalExceptionMiddleware {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger) {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context) {
        try {
            await _next(context);
        } catch (Exception ex) when (IsDependencyFailure(ex)) {
            _logger.LogError(ex, "Dependency failure while processing {Path} (TraceId={TraceId}).", context.Request.Path, context.TraceIdentifier);
            await WriteProblemAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "Service Unavailable",
                "DEPENDENCY_UNAVAILABLE",
                "A required service dependency is temporarily unavailable."
            );
        } catch (Exception ex) {
            _logger.LogError(ex, "Unhandled exception while processing {Path} (TraceId={TraceId}).", context.Request.Path, context.TraceIdentifier);
            await WriteProblemAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                "INTERNAL_ERROR",
                "An unexpected error occurred."
            );
        }
    }

    private static bool IsDependencyFailure(Exception ex) {
        return ex is SqlException or TimeoutException
            || (ex is DbUpdateException dbEx && dbEx.InnerException is SqlException)
            || (ex is InvalidOperationException && ex.InnerException is SqlException);
    }

    private static async Task WriteProblemAsync(HttpContext context, int status, string title, string code, string detail) {
        if (context.Response.HasStarted) {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        var problem = new ApiProblemDetails(
            "about:blank",
            title,
            status,
            detail,
            context.Request.Path.Value ?? "/",
            code,
            context.TraceIdentifier
        );

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }
}
