using Gateway.API.Infrastructure.Http;
using Yarp.ReverseProxy.Forwarder;

namespace Gateway.API.Infrastructure.Middleware;

public sealed class UpstreamErrorProblemMiddleware {
    private readonly RequestDelegate _next;
    private readonly ILogger<UpstreamErrorProblemMiddleware> _logger;

    public UpstreamErrorProblemMiddleware(RequestDelegate next, ILogger<UpstreamErrorProblemMiddleware> logger) {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context) {
        context.Items[GlobalExceptionMiddleware.ProxiedItemKey] = true;

        await _next(context);

        var errorFeature = context.Features.Get<IForwarderErrorFeature>();
        if (errorFeature is not null && !context.Response.HasStarted) {
            _logger.LogWarning(
                errorFeature.Exception,
                "Upstream proxy error {ForwarderError} on {Path} (TraceId={TraceId}).",
                errorFeature.Error,
                context.Request.Path,
                context.TraceIdentifier
            );

            await ProblemResponseWriter.WriteAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "Service Unavailable",
                "DEPENDENCY_UNAVAILABLE",
                "The upstream service is temporarily unavailable."
            );
        }
    }
}
