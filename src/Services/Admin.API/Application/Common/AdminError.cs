namespace Admin.API.Application.Common;

public sealed record AdminError(
    int StatusCode,
    string Title,
    string Code,
    string Detail,
    string[]? Errors = null
);
