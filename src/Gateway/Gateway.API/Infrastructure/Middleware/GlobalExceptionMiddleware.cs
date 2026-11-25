using Gateway.API.Infrastructure.Http;

namespace Gateway.API.Infrastructure.Middleware;

public sealed class GlobalExceptionMiddleware {
    public const string ProxiedItemKey = "GatewayProxied";

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger) {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context) {
        try {
            await _next(context);

            var wasProxied = context.Items.TryGetValue(ProxiedItemKey, out var value) && value is true;
            if (!wasProxied && !context.Response.HasStarted && context.Response.ContentLength is null) {
                if (context.Response.StatusCode == StatusCodes.Status404NotFound) {
                    await ProblemResponseWriter.WriteAsync(
                        context,
                        StatusCodes.Status404NotFound,
                        "Not Found",
                        "ROUTE_NOT_FOUND",
                        "The requested endpoint does not exist."
                    );
                } else if (context.Response.StatusCode == StatusCodes.Status405MethodNotAllowed) {
                    await ProblemResponseWriter.WriteAsync(
                        context,
                        StatusCodes.Status405MethodNotAllowed,
                        "Method Not Allowed",
                        "METHOD_NOT_ALLOWED",
                        "The HTTP method is not allowed for this endpoint."
                    );
                }
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "Unhandled exception in Gateway while processing {Path} (TraceId={TraceId}).", context.Request.Path, context.TraceIdentifier);
            await ProblemResponseWriter.WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                "INTERNAL_ERROR",
                "An unexpected error occurred."
            );
        }
    }
}
