using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inventory.API.Infrastructure.Http;

public static class ProblemDetailsHelper
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static ApiProblemDetails Create(
        HttpContext context,
        int statusCode,
        string code,
        string detail,
        string? title = null,
        IReadOnlyList<string>? errors = null)
    {
        var traceId = context.Items.TryGetValue(CorrelationIdMiddleware.ContextItemKey, out var value)
            && value is string correlationId
            && !string.IsNullOrWhiteSpace(correlationId)
            ? correlationId
            : context.TraceIdentifier;

        return new ApiProblemDetails
        {
            Type = "about:blank",
            Title = title ?? GetDefaultTitle(statusCode),
            Status = statusCode,
            Detail = detail,
            Instance = context.Request.Path.Value ?? "/",
            Code = code,
            TraceId = traceId,
            Errors = errors is { Count: > 0 } ? errors : null
        };
    }

    public static async Task WriteAsync(
        HttpContext context,
        int statusCode,
        string code,
        string detail,
        string? title = null,
        IReadOnlyList<string>? errors = null)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        var problem = Create(context, statusCode, code, detail, title, errors);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, SerializerOptions));
    }

    public static string GetDefaultTitle(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status422UnprocessableEntity => "Unprocessable Entity",
        StatusCodes.Status429TooManyRequests => "Too Many Requests",
        StatusCodes.Status503ServiceUnavailable => "Service Unavailable",
        _ => "Internal Server Error"
    };
}