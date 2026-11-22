using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wallet.API.Application.Exceptions;
using Wallet.API.Domain.Errors;

namespace Wallet.API.Infrastructure.Http;

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
        catch (WalletApiException ex)
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
                WalletErrorCodes.ValidationError,
                "The request payload is malformed or invalid.");
        }
        catch (Exception ex) when (ex is DbException or TimeoutException || (ex is DbUpdateException && ex.InnerException is DbException))
        {
            _logger.LogError(ex, "Database or dependency failure while handling {Path}", context.Request.Path);
            await ProblemDetailsHelper.WriteAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                WalletErrorCodes.DependencyUnavailable,
                "A required dependency is currently unavailable.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while handling {Path}", context.Request.Path);
            await ProblemDetailsHelper.WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                WalletErrorCodes.InternalError,
                "An unexpected internal error occurred.");
        }
    }
}