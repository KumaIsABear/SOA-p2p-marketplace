using System.Text.Json;
using Gateway.API.Application.DTOs;

namespace Gateway.API.Infrastructure.Http;

public static class ProblemResponseWriter {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task WriteAsync(
        HttpContext context,
        int status,
        string title,
        string code,
        string detail,
        string[]? errors = null
    ) {
        if (context.Response.HasStarted) {
            return;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        var problem = new ApiProblemDetails(
            "about:blank",
            title,
            status,
            detail,
            context.Request.Path.Value ?? "/",
            code,
            context.TraceIdentifier,
            errors
        );

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }
}
