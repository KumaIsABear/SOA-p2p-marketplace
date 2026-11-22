using System.Text.Json;
using Marketplace.API.Application.Common;
using Marketplace.API.Application.DTOs;
using Microsoft.AspNetCore.WebUtilities;

namespace Marketplace.API.Infrastructure.Http;

public sealed class GlobalExceptionMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (MarketplaceApiException ex)
        {
            await WriteProblemAsync(context, ex.StatusCode, ex.Code, ex.Message, ex.Errors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while processing {Path}.", context.Request.Path);
            await WriteProblemAsync(
                context,
                StatusCodes.Status500InternalServerError,
                MarketplaceErrorCodes.InternalError,
                "An unexpected error occurred.",
                null
            );
        }
    }

    public static async Task WriteProblemAsync(
        HttpContext context,
        int statusCode,
        string code,
        string detail,
        string[]? errors = null)
    {
        var traceId = context.Items[CorrelationIdMiddleware.ItemKey]?.ToString() ?? context.TraceIdentifier;

        var problem = new ApiProblemDetails(
            Type: "about:blank",
            Title: ReasonPhrases.GetReasonPhrase(statusCode),
            Status: statusCode,
            Detail: detail,
            Instance: context.Request.Path.Value ?? "/",
            Code: code,
            TraceId: traceId,
            Errors: errors
        );

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }
}
