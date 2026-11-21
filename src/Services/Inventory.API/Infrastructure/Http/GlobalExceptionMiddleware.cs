using System.Data.Common;
using System.Text.Json;
using Inventory.API.Application.Exceptions;
using Inventory.API.Domain.Errors;

namespace Inventory.API.Infrastructure.Http;

public sealed class GlobalExceptionMiddleware
{
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
        catch (InventoryApiException ex)
        {
            await ProblemDetailsHelper.WriteAsync(
                context,
                ex.StatusCode,
                ex.Code,
                ex.Detail,
                ex.Title,
                ex.Errors);
        }
        catch (Exception ex) when (ex is BadHttpRequestException or JsonException)
        {
            await ProblemDetailsHelper.WriteAsync(
                context,
                StatusCodes.Status400BadRequest,
                InventoryErrorCodes.ValidationError,
                "The request payload is malformed or invalid.");
        }
        catch (Exception ex) when (ex is DbException or TimeoutException)
        {
            _logger.LogError(ex, "Database or dependency failure while handling {Path}", context.Request.Path);
            await ProblemDetailsHelper.WriteAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                InventoryErrorCodes.DependencyUnavailable,
                "A required dependency is currently unavailable.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while handling {Path}", context.Request.Path);
            await ProblemDetailsHelper.WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                InventoryErrorCodes.InternalError,
                "An unexpected internal error occurred.");
        }
    }
}