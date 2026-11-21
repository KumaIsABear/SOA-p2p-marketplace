using System.Text.Json.Serialization;

namespace Inventory.API.Infrastructure.Http;

public sealed class ApiProblemDetails
{
    public string Type { get; init; } = "about:blank";
    public string Title { get; init; } = string.Empty;
    public int Status { get; init; }
    public string Detail { get; init; } = string.Empty;
    public string Instance { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Errors { get; init; }
}