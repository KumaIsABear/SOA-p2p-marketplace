using System.Text.Json.Serialization;

namespace Gateway.API.Application.DTOs;

public sealed record ApiProblemDetails(
    string Type,
    string Title,
    int Status,
    string Detail,
    string Instance,
    string Code,
    string TraceId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? Errors = null
);
