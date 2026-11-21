using Inventory.API.Infrastructure.Http;

namespace Inventory.API.Application.Exceptions;

public sealed class InventoryApiException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }
    public string Title { get; }
    public string Detail { get; }
    public IReadOnlyList<string>? Errors { get; }

    public InventoryApiException(
        int statusCode,
        string code,
        string detail,
        string? title = null,
        IReadOnlyList<string>? errors = null)
        : base(detail)
    {
        StatusCode = statusCode;
        Code = code;
        Title = title ?? ProblemDetailsHelper.GetDefaultTitle(statusCode);
        Detail = detail;
        Errors = errors;
    }
}